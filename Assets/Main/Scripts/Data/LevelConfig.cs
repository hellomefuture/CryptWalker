using UnityEngine;

namespace CryptWalker.Main.Data
{
    /// <summary>
    /// 关卡数据配置（ScriptableObject）—— 方案 B：Grid-based 完全程序化生成。
    ///
    /// 数据流：
    ///   场景里的 LevelConfigHolder (AOT MonoBehaviour) SerializeField 持有本 SO 引用
    ///   → Awake 时通过 AppBridge.SetLevelConfig(config) 注入桥接层
    ///   → HotUpdate 端 GameFlowMachine 通过 IAppBridge.GetLevelConfig() 拿
    ///   → DungeonGenerator 接收 config，用 playerPrefab / orc1Prefab 实例化实体，
    ///     用 roomTileset / gridSize / roomCount / seed 程序化生成所有房间和走廊
    ///
    /// 优势：
    ///   1) 抛弃所有房间 prefab（Start/Combat/End 不再需要美术资产）
    ///   2) 用纯算法 + 2 个 Tile 资产就能生成完整关卡
    ///   3) 不破坏 HotUpdate DLL 严禁引用 AOT API 的硬约束（SO 是配置类型，是允许的例外）
    /// </summary>
    [CreateAssetMenu(menuName = "CryptWalker/LevelConfig", fileName = "LevelConfig")]
    public class LevelConfig : ScriptableObject
    {
        [Header("Player")]
        public GameObject playerPrefab;

        [Header("Enemies")]
        public GameObject orc1Prefab;
        public GameObject orc2Prefab;
        public GameObject orc3Prefab;

        [Header("Dungeon Gen (Grid-based)")]
        [Tooltip("程序化生成所用 Tile 集合（Floor + Wall）")]
        public RoomTileset roomTileset;

        [Tooltip("网格边长（GRID_SIZE × GRID_SIZE 个潜在房间位置）")]
        [Range(3, 10)] public int gridSize = 5;

        [Tooltip("单个房间格子宽")]
        [Range(10, 50)] public int roomWidth = 21;

        [Tooltip("单个房间格子高")]
        [Range(8, 30)] public int roomHeight = 15;

        [Tooltip("走廊宽度（格子数，必须为奇数以保证门洞居中）")]
        [Range(2, 6)] public int corridorWidth = 3;

        [Tooltip("总房间数（含 Start 和 End），例如 6 = Start + 4 Combat + End")]
        [Range(3, 12)] public int roomCount = 6;

        [Tooltip("随机种子（保证同一关卡布局可复现）")]
        public int seed = 42;
    }
}