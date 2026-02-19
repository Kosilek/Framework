using System;
using UnityEngine;

namespace _FM.Appboot.Service
{
    public static class AppbootService
    {
        public static event Action OnAppboot;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void OnRuntimeStart()
        {
            OnAppboot?.Invoke();
        }

        public static void AddListener(Action listener)
        {
            OnAppboot += listener;
        }

        public static void RemoveListener(Action listener)
        {
            OnAppboot -= listener;
        }
    }
}