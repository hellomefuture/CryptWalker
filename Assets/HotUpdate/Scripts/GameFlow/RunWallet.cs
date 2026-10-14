using CryptWalker.HotUpdate.Core;
using CryptWalker.HotUpdate.Entity.Enemy;

namespace CryptWalker.HotUpdate.GameFlow
{
    /// <summary>
    /// 局内钱包（策划案 §二 局内循环：「击杀敌人获取经验/金币/掉落」）。
    ///
    /// 职责很窄：订阅 EnemyDied → 按敌人 CoinDrop 加钱 → 广播 CoinChanged 给 HUD。
    ///
    /// 为什么单独一个模块而不是挂在玩家身上：
    ///   金币是【局内货币】，不是角色属性 —— 它属于"这一局"，不随玩家实体存活。
    ///   所以它由 GameFlow 的 GameState 在开新一局时清零（ResetRun），
    ///   而不是写在 PlayerController 里（那会让"开新一局要重置什么"散在两处）。
    ///
    /// 当前实现是"击杀直接到账"。等掉落物 prefab 到位后，改成
    /// "敌人死亡时生成金币掉落物 → 玩家拾取 → 调用 AddCoin"即可，本类不用动。
    /// </summary>
    public class RunWallet : IModule
    {
        public static RunWallet Instance { get; private set; }

        /// <summary>本局累计金币</summary>
        public int Coin { get; private set; }

        public void Init()
        {
            Instance = this;
            EventBus.Subscribe(GameEvent.EnemyDied, OnEnemyDied);
            Log.Info("[RunWallet] 局内钱包就绪（击杀 → 金币 → CoinChanged）");
        }

        public void Shutdown()
        {
            EventBus.Unsubscribe(GameEvent.EnemyDied, OnEnemyDied);
            Instance = null;
        }

        /// <summary>开新一局：清零并广播（由 GameFlowMachine.GameState 进入战斗时调用）</summary>
        public void ResetRun()
        {
            Coin = 0;
            EventBus.Raise(GameEvent.CoinChanged, Coin);
            Log.Info("[RunWallet] 新一局，金币清零");
        }

        /// <summary>加金币（掉落物 / 拾取 / 商店回退 都走这里）</summary>
        public void AddCoin(int amount)
        {
            if (amount <= 0) return;
            Coin += amount;
            EventBus.Raise(GameEvent.CoinChanged, Coin);
        }

        /// <summary>花金币：够才扣，返回是否成功（商店用）</summary>
        public bool TrySpend(int amount)
        {
            if (amount <= 0 || Coin < amount) return false;
            Coin -= amount;
            EventBus.Raise(GameEvent.CoinChanged, Coin);
            return true;
        }

        private void OnEnemyDied(object payload)
        {
            if (payload is EnemyBase enemy) AddCoin(enemy.CoinDrop);
        }
    }
}
