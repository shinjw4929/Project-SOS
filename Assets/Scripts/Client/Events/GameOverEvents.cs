using System;

namespace Client
{
    /// <summary>
    /// 게임오버 관련 이벤트 (ECS → MonoBehaviour 통신용)
    /// </summary>
    public static class GameOverEvents
    {
        /// <summary>
        /// 내 히어로가 죽었을 때 발생
        /// </summary>
        public static event Action OnHeroDeath;

        /// <summary>
        /// 게임오버 시 발생
        /// </summary>
        public static event Action OnGameOver;

        public static void RaiseHeroDeath() => OnHeroDeath?.Invoke();
        public static void RaiseGameOver() => OnGameOver?.Invoke();

        /// <summary>
        /// 이벤트 정리 (씬 전환 시 호출)
        /// </summary>
        public static void Clear()
        {
            OnHeroDeath = null;
            OnGameOver = null;
        }
    }
}
