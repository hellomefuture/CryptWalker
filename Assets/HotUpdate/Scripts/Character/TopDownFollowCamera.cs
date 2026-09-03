using UnityEngine;

namespace CryptWalker.Character
{
    /// <summary>
    /// 简单 2D 俯视摄像机: 跟随目标,保持 Z=-10。
    /// 毕设 Demo 用,不上 SmoothDamp 之类花哨的。
    /// </summary>
    public class TopDownFollowCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private float z = -10f;

        private void LateUpdate()
        {
            if (target == null) return;
            var p = target.position;
            transform.position = new Vector3(p.x, p.y, z);
        }
    }
}
