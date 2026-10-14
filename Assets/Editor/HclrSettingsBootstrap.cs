using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using HybridCLR.Editor.Settings;

namespace CryptWalker.EditorTools
{
    /// <summary>
    /// 每次编辑器加载 / 每次脚本重编译后，用代码把 HybridCLR 的设置对象"钉"进
    /// HybridCLRSettings.s_Instance，并**确保磁盘上那个读不回来的设置文件不存在**。
    ///
    /// ===== 为什么需要这个 =====
    /// HybridCLR v8.5.1 的 HybridCLRSettings.LoadOrCreate() 是：
    ///
    ///     Object[] objs = InternalEditorUtility.LoadSerializedFileAndForget(path);
    ///     s_Instance = objs.Length > 0 ? (HybridCLRSettings)objs[0]
    ///                                  : (s_Instance ?? CreateInstance<HybridCLRSettings>());
    ///
    /// 它有两个问题（本机已实测复现）：
    ///   1) Save() 写出的文件里 m_Script 是空引用，而 LoadSerializedFileAndForget
    ///      解析不了空引用（不像场景/预制体有 ScriptMapper 兜底），会返回占位对象
    ///      UnityEditor.FallbackEditorWindow → 强转 HybridCLRSettings 直接抛
    ///      InvalidCastException；
    ///   2) 即使手工把 m_Script 改成正确的 MonoScript 引用，本机环境下依然解析不出类型
    ///      （MonoScript.GetClass() 返回 null），文件照样读不回来。
    ///
    /// 结果就是：只要文件存在、且发生一次脚本重编译，s_Instance 就会被重新赋成坏值，
    /// 于是 Generate/All、Build、以及 HybridCLR 设置窗口全部失败，而且无法自助恢复。
    ///
    /// ===== 这个脚本怎么绕过去 =====
    ///   · 删掉那个读不回来的设置文件 → objs.Length == 0 → 走 `s_Instance ?? CreateInstance`
    ///     这一支。此时只要 s_Instance 里已经有有效对象，它会被**保留**而不是被覆盖；
    ///   · 于是每次加载都主动注入一个配置好的有效对象，HybridCLR 拿到的永远是好的。
    ///
    /// 所以本文件是"以代码为准"的配置来源 —— 要改热更程序集清单，改下面 Apply() 即可。
    ///
    /// 另外给注入的对象设了 HideFlags.DontSave。这一步不能省：不设的话 Unity 会把它
    /// 当"未使用的临时对象"销毁掉，静态字段就变成"假 null"（Unity 的 == 认为它是 null，
    /// 但 C# 的 ?? 判不出来），那正是之前 "Object at index 0 is null" 的来源。
    ///
    /// 说明：这是绕开第三方包缺陷的临时方案。等查清并修好脚本映射问题后，
    /// 可以把它连同 ProjectSettings/HybridCLRSettings.asset 一起恢复成正常用法。
    /// </summary>
    [InitializeOnLoad]
    public static class HclrSettingsBootstrap
    {
        private const string SettingsPath = "ProjectSettings/HybridCLRSettings.asset";
        private const string HotUpdateAsmdefPath = "Assets/HotUpdate/Scripts/CryptWalker.HotUpdate.asmdef";

        static HclrSettingsBootstrap()
        {
            // 用 delayCall：静态构造期 AssetDatabase 还不一定可用，等编辑器就绪再动手
            EditorApplication.delayCall += () => Apply(false);
        }

        [MenuItem("CryptWalker/重新应用 HybridCLR 设置（诊断）", priority = 900)]
        private static void ApplyFromMenu()
        {
            Apply(true);
        }

        private static void Apply(bool verbose)
        {
            var sb = new StringBuilder();
            try
            {
                // ---------- 1) 确保设置文件不存在 ----------
                // 这一步是前提：文件存在时 LoadOrCreate 会用它覆盖我们的注入（甚至抛异常）。
                if (File.Exists(SettingsPath))
                {
                    File.Delete(SettingsPath);
                    sb.AppendLine($"[HCLR] 已删除读不回来的设置文件：{SettingsPath}");
                }

                // ---------- 2) 造一个配置好的有效设置对象 ----------
                var settings = ScriptableObject.CreateInstance<HybridCLRSettings>();
                settings.hideFlags = HideFlags.DontSave;   // 防止 Unity 把它当无用对象销毁（见类注释）
                settings.enable = true;
                settings.hotUpdateDllCompileOutputRootDir = "HybridCLRData/HotUpdateDlls";
                settings.strippedAOTDllOutputRootDir = "HybridCLRData/AssembliesPostIl2CppStrip";
                // AOT 补充元数据清单（HybridCLR Installer 的默认值）。
                // 构建脚本按这个清单把 dll 拷进 StreamingAssets/HybridCLR/AOTMetadata。
                // 注意：字段没有默认值，不设就是 null → 出包会少带 AOT 元数据 → 热更侧泛型调用失败。
                settings.patchAOTAssemblies = new[] { "mscorlib", "System", "System.Core" };

                var asmdef = AssetDatabase.LoadAssetAtPath<AssemblyDefinitionAsset>(HotUpdateAsmdefPath);
                settings.hotUpdateAssemblyDefinitions = new[] { asmdef };

                // ---------- 3) 注入静态字段 s_Instance ----------
                var field = typeof(HybridCLRSettings).GetField(
                    "s_Instance", BindingFlags.NonPublic | BindingFlags.Static);
                field?.SetValue(null, settings);

                // ---------- 4) 自检 ----------
                bool ok = HybridCLRSettings.Instance != null;
                if (verbose || !ok)
                {
                    sb.AppendLine($"[HCLR] 注入 s_Instance：{(field != null ? "成功" : "【失败】字段名变了")}");
                    sb.AppendLine($"[HCLR] 热更程序集：{(asmdef ? asmdef.name : "【失败】路径不对")}");
                    sb.AppendLine($"[HCLR] 自检 Instance：{(ok ? "可用 ✓" : "不可用 ✗")}");
                    Debug.Log(sb.ToString());
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[HCLR] 应用 HybridCLR 设置失败：{e}");
            }
        }
    }
}
