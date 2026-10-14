using System.Collections.Generic;
public class AOTGenericReferences : UnityEngine.MonoBehaviour
{

	// {{ AOT assemblies
	public static readonly IReadOnlyList<string> PatchedAOTAssemblyList = new List<string>
	{
		"CryptWalker.Main.Runtime.dll",
		"System.Core.dll",
		"UnityEngine.CoreModule.dll",
		"mscorlib.dll",
	};
	// }}

	// {{ constraint implement type
	// }} 

	// {{ AOT generic types
	// System.Action<CryptWalker.HotUpdate.UI.MainMenuPanel.ParallaxLayer>
	// System.Action<UnityEngine.Vector2Int>
	// System.Action<UnityEngine.Vector3>
	// System.Action<float>
	// System.Action<int,int,float>
	// System.Action<int>
	// System.Action<object,int>
	// System.Action<object,object>
	// System.Action<object>
	// System.Collections.Generic.ArraySortHelper<CryptWalker.HotUpdate.UI.MainMenuPanel.ParallaxLayer>
	// System.Collections.Generic.ArraySortHelper<UnityEngine.Vector2Int>
	// System.Collections.Generic.ArraySortHelper<UnityEngine.Vector3>
	// System.Collections.Generic.ArraySortHelper<int>
	// System.Collections.Generic.ArraySortHelper<object>
	// System.Collections.Generic.Comparer<CryptWalker.HotUpdate.UI.MainMenuPanel.ParallaxLayer>
	// System.Collections.Generic.Comparer<UnityEngine.Vector2Int>
	// System.Collections.Generic.Comparer<UnityEngine.Vector3>
	// System.Collections.Generic.Comparer<int>
	// System.Collections.Generic.Comparer<object>
	// System.Collections.Generic.Dictionary.Enumerator<int,float>
	// System.Collections.Generic.Dictionary.Enumerator<int,int>
	// System.Collections.Generic.Dictionary.Enumerator<int,object>
	// System.Collections.Generic.Dictionary.Enumerator<object,object>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<int,float>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<int,int>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<int,object>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<object,object>
	// System.Collections.Generic.Dictionary.KeyCollection<int,float>
	// System.Collections.Generic.Dictionary.KeyCollection<int,int>
	// System.Collections.Generic.Dictionary.KeyCollection<int,object>
	// System.Collections.Generic.Dictionary.KeyCollection<object,object>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<int,float>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<int,int>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<int,object>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<object,object>
	// System.Collections.Generic.Dictionary.ValueCollection<int,float>
	// System.Collections.Generic.Dictionary.ValueCollection<int,int>
	// System.Collections.Generic.Dictionary.ValueCollection<int,object>
	// System.Collections.Generic.Dictionary.ValueCollection<object,object>
	// System.Collections.Generic.Dictionary<int,float>
	// System.Collections.Generic.Dictionary<int,int>
	// System.Collections.Generic.Dictionary<int,object>
	// System.Collections.Generic.Dictionary<object,object>
	// System.Collections.Generic.EqualityComparer<System.ValueTuple<UnityEngine.Vector2Int,UnityEngine.Vector2Int>>
	// System.Collections.Generic.EqualityComparer<UnityEngine.Vector2Int>
	// System.Collections.Generic.EqualityComparer<float>
	// System.Collections.Generic.EqualityComparer<int>
	// System.Collections.Generic.EqualityComparer<object>
	// System.Collections.Generic.HashSet.Enumerator<System.ValueTuple<UnityEngine.Vector2Int,UnityEngine.Vector2Int>>
	// System.Collections.Generic.HashSet.Enumerator<object>
	// System.Collections.Generic.HashSet<System.ValueTuple<UnityEngine.Vector2Int,UnityEngine.Vector2Int>>
	// System.Collections.Generic.HashSet<object>
	// System.Collections.Generic.HashSetEqualityComparer<System.ValueTuple<UnityEngine.Vector2Int,UnityEngine.Vector2Int>>
	// System.Collections.Generic.HashSetEqualityComparer<object>
	// System.Collections.Generic.ICollection<CryptWalker.HotUpdate.UI.MainMenuPanel.ParallaxLayer>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<int,float>>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<int,int>>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<int,object>>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<object,object>>
	// System.Collections.Generic.ICollection<System.ValueTuple<UnityEngine.Vector2Int,UnityEngine.Vector2Int>>
	// System.Collections.Generic.ICollection<UnityEngine.Vector2Int>
	// System.Collections.Generic.ICollection<UnityEngine.Vector3>
	// System.Collections.Generic.ICollection<int>
	// System.Collections.Generic.ICollection<object>
	// System.Collections.Generic.IComparer<CryptWalker.HotUpdate.UI.MainMenuPanel.ParallaxLayer>
	// System.Collections.Generic.IComparer<UnityEngine.Vector2Int>
	// System.Collections.Generic.IComparer<UnityEngine.Vector3>
	// System.Collections.Generic.IComparer<int>
	// System.Collections.Generic.IComparer<object>
	// System.Collections.Generic.IEnumerable<CryptWalker.HotUpdate.UI.MainMenuPanel.ParallaxLayer>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<int,float>>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<int,int>>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<int,object>>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<object,object>>
	// System.Collections.Generic.IEnumerable<System.ValueTuple<UnityEngine.Vector2Int,UnityEngine.Vector2Int>>
	// System.Collections.Generic.IEnumerable<UnityEngine.Vector2Int>
	// System.Collections.Generic.IEnumerable<UnityEngine.Vector3>
	// System.Collections.Generic.IEnumerable<int>
	// System.Collections.Generic.IEnumerable<object>
	// System.Collections.Generic.IEnumerator<CryptWalker.HotUpdate.UI.MainMenuPanel.ParallaxLayer>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<int,float>>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<int,int>>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<int,object>>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<object,object>>
	// System.Collections.Generic.IEnumerator<System.ValueTuple<UnityEngine.Vector2Int,UnityEngine.Vector2Int>>
	// System.Collections.Generic.IEnumerator<UnityEngine.Vector2Int>
	// System.Collections.Generic.IEnumerator<UnityEngine.Vector3>
	// System.Collections.Generic.IEnumerator<int>
	// System.Collections.Generic.IEnumerator<object>
	// System.Collections.Generic.IEqualityComparer<System.ValueTuple<UnityEngine.Vector2Int,UnityEngine.Vector2Int>>
	// System.Collections.Generic.IEqualityComparer<int>
	// System.Collections.Generic.IEqualityComparer<object>
	// System.Collections.Generic.IList<CryptWalker.HotUpdate.UI.MainMenuPanel.ParallaxLayer>
	// System.Collections.Generic.IList<UnityEngine.Vector2Int>
	// System.Collections.Generic.IList<UnityEngine.Vector3>
	// System.Collections.Generic.IList<int>
	// System.Collections.Generic.IList<object>
	// System.Collections.Generic.IReadOnlyCollection<object>
	// System.Collections.Generic.KeyValuePair<int,float>
	// System.Collections.Generic.KeyValuePair<int,int>
	// System.Collections.Generic.KeyValuePair<int,object>
	// System.Collections.Generic.KeyValuePair<object,object>
	// System.Collections.Generic.List.Enumerator<CryptWalker.HotUpdate.UI.MainMenuPanel.ParallaxLayer>
	// System.Collections.Generic.List.Enumerator<UnityEngine.Vector2Int>
	// System.Collections.Generic.List.Enumerator<UnityEngine.Vector3>
	// System.Collections.Generic.List.Enumerator<int>
	// System.Collections.Generic.List.Enumerator<object>
	// System.Collections.Generic.List<CryptWalker.HotUpdate.UI.MainMenuPanel.ParallaxLayer>
	// System.Collections.Generic.List<UnityEngine.Vector2Int>
	// System.Collections.Generic.List<UnityEngine.Vector3>
	// System.Collections.Generic.List<int>
	// System.Collections.Generic.List<object>
	// System.Collections.Generic.ObjectComparer<CryptWalker.HotUpdate.UI.MainMenuPanel.ParallaxLayer>
	// System.Collections.Generic.ObjectComparer<UnityEngine.Vector2Int>
	// System.Collections.Generic.ObjectComparer<UnityEngine.Vector3>
	// System.Collections.Generic.ObjectComparer<int>
	// System.Collections.Generic.ObjectComparer<object>
	// System.Collections.Generic.ObjectEqualityComparer<System.ValueTuple<UnityEngine.Vector2Int,UnityEngine.Vector2Int>>
	// System.Collections.Generic.ObjectEqualityComparer<UnityEngine.Vector2Int>
	// System.Collections.Generic.ObjectEqualityComparer<float>
	// System.Collections.Generic.ObjectEqualityComparer<int>
	// System.Collections.Generic.ObjectEqualityComparer<object>
	// System.Collections.Generic.Queue.Enumerator<object>
	// System.Collections.Generic.Queue<object>
	// System.Collections.ObjectModel.ReadOnlyCollection<CryptWalker.HotUpdate.UI.MainMenuPanel.ParallaxLayer>
	// System.Collections.ObjectModel.ReadOnlyCollection<UnityEngine.Vector2Int>
	// System.Collections.ObjectModel.ReadOnlyCollection<UnityEngine.Vector3>
	// System.Collections.ObjectModel.ReadOnlyCollection<int>
	// System.Collections.ObjectModel.ReadOnlyCollection<object>
	// System.Comparison<CryptWalker.HotUpdate.UI.MainMenuPanel.ParallaxLayer>
	// System.Comparison<UnityEngine.Vector2Int>
	// System.Comparison<UnityEngine.Vector3>
	// System.Comparison<int>
	// System.Comparison<object>
	// System.Func<System.Threading.Tasks.VoidTaskResult>
	// System.Func<byte>
	// System.Func<object,System.Threading.Tasks.VoidTaskResult>
	// System.Func<object,byte>
	// System.Func<object,object>
	// System.Func<object>
	// System.Predicate<CryptWalker.HotUpdate.UI.MainMenuPanel.ParallaxLayer>
	// System.Predicate<System.ValueTuple<UnityEngine.Vector2Int,UnityEngine.Vector2Int>>
	// System.Predicate<UnityEngine.Vector2Int>
	// System.Predicate<UnityEngine.Vector3>
	// System.Predicate<int>
	// System.Predicate<object>
	// System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>
	// System.Runtime.CompilerServices.AsyncTaskMethodBuilder<byte>
	// System.Runtime.CompilerServices.AsyncTaskMethodBuilder<object>
	// System.Runtime.CompilerServices.ConfiguredTaskAwaitable.ConfiguredTaskAwaiter<System.Threading.Tasks.VoidTaskResult>
	// System.Runtime.CompilerServices.ConfiguredTaskAwaitable.ConfiguredTaskAwaiter<byte>
	// System.Runtime.CompilerServices.ConfiguredTaskAwaitable.ConfiguredTaskAwaiter<object>
	// System.Runtime.CompilerServices.ConfiguredTaskAwaitable<System.Threading.Tasks.VoidTaskResult>
	// System.Runtime.CompilerServices.ConfiguredTaskAwaitable<byte>
	// System.Runtime.CompilerServices.ConfiguredTaskAwaitable<object>
	// System.Runtime.CompilerServices.TaskAwaiter<System.Threading.Tasks.VoidTaskResult>
	// System.Runtime.CompilerServices.TaskAwaiter<byte>
	// System.Runtime.CompilerServices.TaskAwaiter<object>
	// System.Threading.Tasks.ContinuationTaskFromResultTask<System.Threading.Tasks.VoidTaskResult>
	// System.Threading.Tasks.ContinuationTaskFromResultTask<byte>
	// System.Threading.Tasks.ContinuationTaskFromResultTask<object>
	// System.Threading.Tasks.Task<System.Threading.Tasks.VoidTaskResult>
	// System.Threading.Tasks.Task<byte>
	// System.Threading.Tasks.Task<object>
	// System.Threading.Tasks.TaskFactory<System.Threading.Tasks.VoidTaskResult>
	// System.Threading.Tasks.TaskFactory<byte>
	// System.Threading.Tasks.TaskFactory<object>
	// System.ValueTuple<UnityEngine.Vector2Int,UnityEngine.Vector2Int>
	// UnityEngine.Events.InvokableCall<UnityEngine.Vector2>
	// UnityEngine.Events.InvokableCall<float>
	// UnityEngine.Events.UnityAction<UnityEngine.Vector2>
	// UnityEngine.Events.UnityAction<float>
	// UnityEngine.Events.UnityEvent<UnityEngine.Vector2>
	// UnityEngine.Events.UnityEvent<float>
	// }}

	public void RefMethods()
	{
		// System.Threading.Tasks.Task<object> CryptWalker.Main.IAppBridge.LoadAssetAsync<object>(string)
		// System.Void System.Array.Resize<float>(float[]&,int)
		// System.Void System.Array.Resize<int>(int[]&,int)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,CryptWalker.HotUpdate.GameFlow.GameFlowMachine.GameState.<OpenHudAsync>d__7>(System.Runtime.CompilerServices.TaskAwaiter&,CryptWalker.HotUpdate.GameFlow.GameFlowMachine.GameState.<OpenHudAsync>d__7&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,CryptWalker.HotUpdate.UI.HUDPanel.<ShowAsync>d__10>(System.Runtime.CompilerServices.TaskAwaiter&,CryptWalker.HotUpdate.UI.HUDPanel.<ShowAsync>d__10&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,CryptWalker.HotUpdate.GameFlow.GameFlowMachine.GameState.<GenerateDungeonAsync>d__4>(System.Runtime.CompilerServices.TaskAwaiter<object>&,CryptWalker.HotUpdate.GameFlow.GameFlowMachine.GameState.<GenerateDungeonAsync>d__4&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,CryptWalker.HotUpdate.GameFlow.GameFlowMachine.GameState.<OpenSettingAsync>d__9>(System.Runtime.CompilerServices.TaskAwaiter<object>&,CryptWalker.HotUpdate.GameFlow.GameFlowMachine.GameState.<OpenSettingAsync>d__9&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,CryptWalker.HotUpdate.GameFlow.GameFlowMachine.MainMenuState.<OpenPanelAsync>d__3>(System.Runtime.CompilerServices.TaskAwaiter<object>&,CryptWalker.HotUpdate.GameFlow.GameFlowMachine.MainMenuState.<OpenPanelAsync>d__3&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,CryptWalker.HotUpdate.GameFlow.GameFlowMachine.MainMenuState.<OpenSettingAsync>d__6>(System.Runtime.CompilerServices.TaskAwaiter<object>&,CryptWalker.HotUpdate.GameFlow.GameFlowMachine.MainMenuState.<OpenSettingAsync>d__6&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,CryptWalker.HotUpdate.UI.HUDPanel.<BuildCoinAsync>d__12>(System.Runtime.CompilerServices.TaskAwaiter<object>&,CryptWalker.HotUpdate.UI.HUDPanel.<BuildCoinAsync>d__12&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,CryptWalker.HotUpdate.UI.HUDPanel.<ShowAsync>d__10>(System.Runtime.CompilerServices.TaskAwaiter<object>&,CryptWalker.HotUpdate.UI.HUDPanel.<ShowAsync>d__10&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,CryptWalker.HotUpdate.GameFlow.GameFlowMachine.GameState.<OpenHudAsync>d__7>(System.Runtime.CompilerServices.TaskAwaiter&,CryptWalker.HotUpdate.GameFlow.GameFlowMachine.GameState.<OpenHudAsync>d__7&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter,CryptWalker.HotUpdate.UI.HUDPanel.<ShowAsync>d__10>(System.Runtime.CompilerServices.TaskAwaiter&,CryptWalker.HotUpdate.UI.HUDPanel.<ShowAsync>d__10&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,CryptWalker.HotUpdate.GameFlow.GameFlowMachine.GameState.<GenerateDungeonAsync>d__4>(System.Runtime.CompilerServices.TaskAwaiter<object>&,CryptWalker.HotUpdate.GameFlow.GameFlowMachine.GameState.<GenerateDungeonAsync>d__4&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,CryptWalker.HotUpdate.GameFlow.GameFlowMachine.GameState.<OpenSettingAsync>d__9>(System.Runtime.CompilerServices.TaskAwaiter<object>&,CryptWalker.HotUpdate.GameFlow.GameFlowMachine.GameState.<OpenSettingAsync>d__9&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,CryptWalker.HotUpdate.GameFlow.GameFlowMachine.MainMenuState.<OpenPanelAsync>d__3>(System.Runtime.CompilerServices.TaskAwaiter<object>&,CryptWalker.HotUpdate.GameFlow.GameFlowMachine.MainMenuState.<OpenPanelAsync>d__3&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,CryptWalker.HotUpdate.GameFlow.GameFlowMachine.MainMenuState.<OpenSettingAsync>d__6>(System.Runtime.CompilerServices.TaskAwaiter<object>&,CryptWalker.HotUpdate.GameFlow.GameFlowMachine.MainMenuState.<OpenSettingAsync>d__6&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,CryptWalker.HotUpdate.UI.HUDPanel.<BuildCoinAsync>d__12>(System.Runtime.CompilerServices.TaskAwaiter<object>&,CryptWalker.HotUpdate.UI.HUDPanel.<BuildCoinAsync>d__12&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<System.Threading.Tasks.VoidTaskResult>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,CryptWalker.HotUpdate.UI.HUDPanel.<ShowAsync>d__10>(System.Runtime.CompilerServices.TaskAwaiter<object>&,CryptWalker.HotUpdate.UI.HUDPanel.<ShowAsync>d__10&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<byte>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.YieldAwaitable.YieldAwaiter,CryptWalker.HotUpdate.AI.AstarPathService.<ScanAsync>d__16>(System.Runtime.CompilerServices.YieldAwaitable.YieldAwaiter&,CryptWalker.HotUpdate.AI.AstarPathService.<ScanAsync>d__16&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<object>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<byte>,CryptWalker.HotUpdate.GameFlow.Dungeon.DungeonGenerator.<GenerateAsync>d__0>(System.Runtime.CompilerServices.TaskAwaiter<byte>&,CryptWalker.HotUpdate.GameFlow.Dungeon.DungeonGenerator.<GenerateAsync>d__0&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<object>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,CryptWalker.HotUpdate.Data.DataService.<GetAsync>d__8<object>>(System.Runtime.CompilerServices.TaskAwaiter<object>&,CryptWalker.HotUpdate.Data.DataService.<GetAsync>d__8<object>&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<object>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.TaskAwaiter<object>,CryptWalker.HotUpdate.UI.UIManager.<OpenPanelAsync>d__12>(System.Runtime.CompilerServices.TaskAwaiter<object>&,CryptWalker.HotUpdate.UI.UIManager.<OpenPanelAsync>d__12&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<object>.AwaitUnsafeOnCompleted<System.Runtime.CompilerServices.YieldAwaitable.YieldAwaiter,CryptWalker.HotUpdate.GameFlow.Dungeon.DungeonGenerator.<GenerateAsync>d__0>(System.Runtime.CompilerServices.YieldAwaitable.YieldAwaiter&,CryptWalker.HotUpdate.GameFlow.Dungeon.DungeonGenerator.<GenerateAsync>d__0&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.Start<CryptWalker.HotUpdate.GameFlow.GameFlowMachine.GameState.<GenerateDungeonAsync>d__4>(CryptWalker.HotUpdate.GameFlow.GameFlowMachine.GameState.<GenerateDungeonAsync>d__4&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.Start<CryptWalker.HotUpdate.GameFlow.GameFlowMachine.GameState.<OpenHudAsync>d__7>(CryptWalker.HotUpdate.GameFlow.GameFlowMachine.GameState.<OpenHudAsync>d__7&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.Start<CryptWalker.HotUpdate.GameFlow.GameFlowMachine.GameState.<OpenSettingAsync>d__9>(CryptWalker.HotUpdate.GameFlow.GameFlowMachine.GameState.<OpenSettingAsync>d__9&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.Start<CryptWalker.HotUpdate.GameFlow.GameFlowMachine.MainMenuState.<OpenPanelAsync>d__3>(CryptWalker.HotUpdate.GameFlow.GameFlowMachine.MainMenuState.<OpenPanelAsync>d__3&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.Start<CryptWalker.HotUpdate.GameFlow.GameFlowMachine.MainMenuState.<OpenSettingAsync>d__6>(CryptWalker.HotUpdate.GameFlow.GameFlowMachine.MainMenuState.<OpenSettingAsync>d__6&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.Start<CryptWalker.HotUpdate.UI.HUDPanel.<BuildCoinAsync>d__12>(CryptWalker.HotUpdate.UI.HUDPanel.<BuildCoinAsync>d__12&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder.Start<CryptWalker.HotUpdate.UI.HUDPanel.<ShowAsync>d__10>(CryptWalker.HotUpdate.UI.HUDPanel.<ShowAsync>d__10&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<byte>.Start<CryptWalker.HotUpdate.AI.AstarPathService.<ScanAsync>d__16>(CryptWalker.HotUpdate.AI.AstarPathService.<ScanAsync>d__16&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<object>.Start<CryptWalker.HotUpdate.Data.DataService.<GetAsync>d__8<object>>(CryptWalker.HotUpdate.Data.DataService.<GetAsync>d__8<object>&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<object>.Start<CryptWalker.HotUpdate.GameFlow.Dungeon.DungeonGenerator.<GenerateAsync>d__0>(CryptWalker.HotUpdate.GameFlow.Dungeon.DungeonGenerator.<GenerateAsync>d__0&)
		// System.Void System.Runtime.CompilerServices.AsyncTaskMethodBuilder<object>.Start<CryptWalker.HotUpdate.UI.UIManager.<OpenPanelAsync>d__12>(CryptWalker.HotUpdate.UI.UIManager.<OpenPanelAsync>d__12&)
		// object UnityEngine.Component.GetComponent<object>()
		// object UnityEngine.Component.GetComponentInChildren<object>(bool)
		// object UnityEngine.Component.GetComponentInParent<object>()
		// object[] UnityEngine.Component.GetComponentsInChildren<object>(bool)
		// bool UnityEngine.Component.TryGetComponent<object>(object&)
		// object UnityEngine.GameObject.AddComponent<object>()
		// object UnityEngine.GameObject.GetComponent<object>()
		// object UnityEngine.GameObject.GetComponentInChildren<object>(bool)
		// object[] UnityEngine.GameObject.GetComponentsInChildren<object>(bool)
		// bool UnityEngine.GameObject.TryGetComponent<object>(object&)
		// object UnityEngine.Object.FindObjectOfType<object>()
		// object[] UnityEngine.Object.FindObjectsOfType<object>()
		// object[] UnityEngine.Object.FindObjectsOfType<object>(bool)
		// object UnityEngine.Object.Instantiate<object>(object)
		// object UnityEngine.Object.Instantiate<object>(object,UnityEngine.Transform)
		// object UnityEngine.Object.Instantiate<object>(object,UnityEngine.Transform,bool)
		// object UnityEngine.Object.Instantiate<object>(object,UnityEngine.Vector3,UnityEngine.Quaternion)
		// object UnityEngine.Object.Instantiate<object>(object,UnityEngine.Vector3,UnityEngine.Quaternion,UnityEngine.Transform)
		// object[] UnityEngine.Resources.ConvertObjects<object>(UnityEngine.Object[])
		// object UnityEngine.Resources.GetBuiltinResource<object>(string)
	}
}