using System;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;

namespace Dashboard
{
    public sealed class UnityDispatcher : MonoBehaviour
    {
        private static UnityDispatcher _instance;
        private static readonly ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();

        public static void Ensure()
        {
            // No-op under IL2CPP to avoid AddComponent at load; we use Plugin's dispatcher instead
            if (_instance == null)
            {
                _instance = null;
            }
        }

        public static void Enqueue(Action action)
        {
            if (action == null) return;
            _queue.Enqueue(action);
        }

        public static T RunSync<T>(Func<T> func, int timeoutMs = 3000)
        {
            if (func == null) throw new ArgumentNullException(nameof(func));

            // If already on main thread, just run
            if (_instance != null && _instance._onMainThread)
            {
                return func();
            }

            var evt = new ManualResetEvent(false);
            T result = default;
            Exception error = null;
            Enqueue(() =>
            {
                try { result = func(); }
                catch (Exception ex) { error = ex; }
                finally { evt.Set(); }
            });
            if (!evt.WaitOne(timeoutMs))
            {
                throw new TimeoutException("UnityDispatcher.RunSync timed out");
            }
            if (error != null) throw error;
            return result;
        }

        private bool _onMainThread;
        private void Awake()
        {
            _onMainThread = true;
        }

        private void Update()
        {
            while (_queue.TryDequeue(out var action))
            {
                try { action?.Invoke(); }
                catch (Exception ex) { ModLogger.Warn($"UnityDispatcher action error: {ex.Message}"); }
            }
        }
    }
}
