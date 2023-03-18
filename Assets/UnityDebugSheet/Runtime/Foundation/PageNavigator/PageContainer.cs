﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityDebugSheet.Runtime.Foundation.PageNavigator.Modules;
using UnityDebugSheet.Runtime.Foundation.PageNavigator.Modules.AssetLoader;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.UI;

namespace UnityDebugSheet.Runtime.Foundation.PageNavigator
{
    [AddComponentMenu("Scripts/Page Container (Unity Debug Sheet)")]
    [RequireComponent(typeof(RectMask2D))]
    public sealed class PageContainer : MonoBehaviour
    {
        private static readonly Dictionary<int, PageContainer> InstanceCacheByTransform =
            new Dictionary<int, PageContainer>();

        private static readonly Dictionary<string, PageContainer> InstanceCacheByName =
            new Dictionary<string, PageContainer>();

        [SerializeField] private string _name;

        private readonly Dictionary<string, AssetLoadHandle<GameObject>> _assetLoadHandles
            = new Dictionary<string, AssetLoadHandle<GameObject>>();

        private readonly List<IPageContainerCallbackReceiver> _callbackReceivers =
            new List<IPageContainerCallbackReceiver>();

        private readonly Dictionary<int, string> _instanceIdToPageId = new Dictionary<int, string>();

        private readonly List<Page> _pages = new List<Page>();

        private readonly Dictionary<string, AssetLoadHandle<GameObject>> _preloadedResourceHandles =
            new Dictionary<string, AssetLoadHandle<GameObject>>();

        private IAssetLoader _assetLoader;

        private CanvasGroup _canvasGroup;

        private bool _isActivePageStacked;

        /// <summary>
        ///     By default, <see cref="IAssetLoader" /> in <see cref="UnityScreenNavigatorSettings" /> is used.
        ///     If this property is set, it is used instead.
        /// </summary>
        public IAssetLoader AssetLoader
        {
            get => _assetLoader ?? PageNavigatorConfig.Instance.AssetLoader;
            set => _assetLoader = value;
        }

        /// <summary>
        ///     True if in transition.
        /// </summary>
        public bool IsInTransition { get; private set; }

        /// <summary>
        ///     Stacked pages.
        /// </summary>
        public IReadOnlyList<Page> Pages => _pages;

        public bool Interactable
        {
            get => _canvasGroup.interactable;
            set => _canvasGroup.interactable = value;
        }

        private void Awake()
        {
            _callbackReceivers.AddRange(GetComponents<IPageContainerCallbackReceiver>());
            if (!string.IsNullOrWhiteSpace(_name))
                InstanceCacheByName.Add(_name, this);

            if (!gameObject.TryGetComponent<CanvasGroup>(out var canvasGroup))
                canvasGroup = gameObject.AddComponent<CanvasGroup>();

            _canvasGroup = canvasGroup;
        }

        private void OnDestroy()
        {
            foreach (var page in _pages)
            {
                var pageInstanceId = page.GetInstanceID();
                var pageId = _instanceIdToPageId[pageInstanceId];
                var assetLoadHandle = _assetLoadHandles[pageId];

                Destroy(page.gameObject);
                AssetLoader.Release(assetLoadHandle);
            }

            _assetLoadHandles.Clear();
            _instanceIdToPageId.Clear();

            InstanceCacheByName.Remove(_name);
            var keysToRemove = new List<int>();
            foreach (var cache in InstanceCacheByTransform)
                if (Equals(cache.Value))
                    keysToRemove.Add(cache.Key);

            foreach (var keyToRemove in keysToRemove)
                InstanceCacheByTransform.Remove(keyToRemove);
        }

        /// <summary>
        ///     Get the <see cref="PageContainer" /> that manages the page to which <see cref="transform" /> belongs.
        /// </summary>
        /// <param name="transform"></param>
        /// <param name="useCache">Use the previous result for the <see cref="transform" />.</param>
        /// <returns></returns>
        public static PageContainer Of(Transform transform, bool useCache = true)
        {
            return Of((RectTransform)transform, useCache);
        }

        /// <summary>
        ///     Get the <see cref="PageContainer" /> that manages the page to which <see cref="rectTransform" /> belongs.
        /// </summary>
        /// <param name="rectTransform"></param>
        /// <param name="useCache">Use the previous result for the <see cref="rectTransform" />.</param>
        /// <returns></returns>
        public static PageContainer Of(RectTransform rectTransform, bool useCache = true)
        {
            var id = rectTransform.GetInstanceID();
            if (useCache && InstanceCacheByTransform.TryGetValue(id, out var container))
                return container;

            container = rectTransform.GetComponentInParent<PageContainer>();
            if (container != null)
            {
                InstanceCacheByTransform.Add(id, container);
                return container;
            }

            return null;
        }

        /// <summary>
        ///     Find the <see cref="PageContainer" /> of <see cref="containerName" />.
        /// </summary>
        /// <param name="containerName"></param>
        /// <returns></returns>
        public static PageContainer Find(string containerName)
        {
            if (InstanceCacheByName.TryGetValue(containerName, out var instance))
                return instance;

            return null;
        }

        /// <summary>
        ///     Add a callback receiver.
        /// </summary>
        /// <param name="callbackReceiver"></param>
        public void AddCallbackReceiver(IPageContainerCallbackReceiver callbackReceiver)
        {
            _callbackReceivers.Add(callbackReceiver);
        }

        /// <summary>
        ///     Remove a callback receiver.
        /// </summary>
        /// <param name="callbackReceiver"></param>
        public void RemoveCallbackReceiver(IPageContainerCallbackReceiver callbackReceiver)
        {
            _callbackReceivers.Remove(callbackReceiver);
        }

        /// <summary>
        ///     Push new page.
        /// </summary>
        /// <param name="resourceKey"></param>
        /// <param name="playAnimation"></param>
        /// <param name="stack"></param>
        /// <param name="pageId"></param>
        /// <param name="loadAsync"></param>
        /// <param name="onLoad"></param>
        /// <returns></returns>
        public AsyncProcessHandle Push(string resourceKey, bool playAnimation, bool stack = true, string pageId = null,
            bool loadAsync = true, Action<(string pageId, Page page)> onLoad = null)
        {
            return CoroutineManager.Instance.Run(PushRoutine(typeof(Page), resourceKey, playAnimation, stack, onLoad,
                loadAsync, pageId));
        }

        /// <summary>
        ///     Push new page.
        /// </summary>
        /// <param name="pageType"></param>
        /// <param name="resourceKey"></param>
        /// <param name="playAnimation"></param>
        /// <param name="stack"></param>
        /// <param name="pageId"></param>
        /// <param name="loadAsync"></param>
        /// <param name="onLoad"></param>
        /// <returns></returns>
        public AsyncProcessHandle Push(Type pageType, string resourceKey, bool playAnimation, bool stack = true,
            string pageId = null, bool loadAsync = true, Action<(string pageId, Page page)> onLoad = null)
        {
            return CoroutineManager.Instance.Run(PushRoutine(pageType, resourceKey, playAnimation, stack, onLoad,
                loadAsync, pageId));
        }

        /// <summary>
        ///     Push new page.
        /// </summary>
        /// <param name="resourceKey"></param>
        /// <param name="playAnimation"></param>
        /// <param name="stack"></param>
        /// <param name="pageId"></param>
        /// <param name="loadAsync"></param>
        /// <param name="onLoad"></param>
        /// <typeparam name="TPage"></typeparam>
        /// <returns></returns>
        public AsyncProcessHandle Push<TPage>(string resourceKey, bool playAnimation, bool stack = true,
            string pageId = null, bool loadAsync = true, Action<(string pageId, Page page)> onLoad = null)
            where TPage : Page
        {
            return CoroutineManager.Instance.Run(PushRoutine(typeof(TPage), resourceKey, playAnimation, stack,
                x => onLoad?.Invoke((x.pageId, (TPage)x.page)), loadAsync, pageId));
        }

        /// <summary>
        ///     Push new page.
        /// </summary>
        /// <param name="playAnimation"></param>
        /// <param name="destinationPageId"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public AsyncProcessHandle Pop(bool playAnimation, string destinationPageId)
        {
            var popCount = 0;
            foreach (var id in _instanceIdToPageId.Values.Reverse())
            {
                if (id == destinationPageId)
                    break;

                popCount++;
            }

            if (popCount == _instanceIdToPageId.Count)
                throw new Exception($"The page with id '{destinationPageId}' is not found.");

            return CoroutineManager.Instance.Run(PopRoutine(playAnimation, popCount));
        }

        /// <summary>
        ///     Pop current page.
        /// </summary>
        /// <param name="playAnimation"></param>
        /// <param name="popCount"></param>
        /// <returns></returns>
        public AsyncProcessHandle Pop(bool playAnimation, int popCount = 1)
        {
            return CoroutineManager.Instance.Run(PopRoutine(playAnimation, popCount));
        }

        private IEnumerator PushRoutine(Type type, string resourceKey, bool playAnimation, bool stack = true,
            Action<(string pageId, Page page)> onLoad = null, bool loadAsync = true, string pageId = null)
        {
            if (resourceKey == null)
                throw new ArgumentNullException(nameof(resourceKey));

            if (IsInTransition)
                throw new InvalidOperationException(
                    "Cannot transition because the screen is already in transition.");

            IsInTransition = true;

            // Setup
            var assetLoadHandle = loadAsync
                ? AssetLoader.LoadAsync<GameObject>(resourceKey)
                : AssetLoader.Load<GameObject>(resourceKey);
            if (!assetLoadHandle.IsDone)
                yield return new WaitUntil(() => assetLoadHandle.IsDone);

            if (assetLoadHandle.Status == AssetLoadStatus.Failed)
                throw assetLoadHandle.OperationException;

            var instance = Instantiate(assetLoadHandle.Result);
            if (!instance.TryGetComponent(type, out var page))
                page = instance.AddComponent(type);
            var enterPage = (Page)page;

            if (pageId == null)
                pageId = Guid.NewGuid().ToString();
            _assetLoadHandles.Add(pageId, assetLoadHandle);
            onLoad?.Invoke((pageId, enterPage));
            var afterLoadHandle = enterPage.AfterLoad((RectTransform)transform);
            while (!afterLoadHandle.IsTerminated)
                yield return null;

            var exitPage = _pages.Count == 0 ? null : _pages[_pages.Count - 1];
            var exitPageInstanceId = exitPage == null ? (int?)null : exitPage.GetInstanceID();
            var exitPageId = exitPageInstanceId.HasValue ? _instanceIdToPageId[exitPageInstanceId.Value] : null;

            // Preprocess
            foreach (var callbackReceiver in _callbackReceivers)
                callbackReceiver.BeforePush(enterPage, exitPage);

            var preprocessHandles = new List<AsyncProcessHandle>();
            if (exitPage != null)
                preprocessHandles.Add(exitPage.BeforeExit(true, enterPage));

            preprocessHandles.Add(enterPage.BeforeEnter(true, exitPage));

            foreach (var coroutineHandle in preprocessHandles)
                while (!coroutineHandle.IsTerminated)
                    yield return coroutineHandle;

            // Play Animations
            var animationHandles = new List<AsyncProcessHandle>();
            if (exitPage != null)
                animationHandles.Add(exitPage.Exit(true, playAnimation, enterPage));

            animationHandles.Add(enterPage.Enter(true, playAnimation, exitPage));

            foreach (var coroutineHandle in animationHandles)
                while (!coroutineHandle.IsTerminated)
                    yield return coroutineHandle;

            // End Transition
            if (!_isActivePageStacked && exitPageId != null)
                _pages.RemoveAt(_pages.Count - 1);

            _pages.Add(enterPage);
            IsInTransition = false;

            // Postprocess
            if (exitPage != null)
                exitPage.AfterExit(true, enterPage);

            enterPage.AfterEnter(true, exitPage);

            foreach (var callbackReceiver in _callbackReceivers)
                callbackReceiver.AfterPush(enterPage, exitPage);

            // Unload Unused Page
            if (!_isActivePageStacked && exitPageId != null)
            {
                var beforeReleaseHandle = exitPage.BeforeRelease();
                while (!beforeReleaseHandle.IsTerminated)
                    yield return null;

                var handle = _assetLoadHandles[exitPageId];
                AssetLoader.Release(handle);

                Destroy(exitPage.gameObject);
                _assetLoadHandles.Remove(exitPageId);
                _instanceIdToPageId.Remove(exitPageInstanceId.Value);
            }

            _isActivePageStacked = stack;
        }

        private IEnumerator PopRoutine(bool playAnimation, int popCount = 1)
        {
            Assert.IsTrue(popCount >= 1);

            if (_pages.Count < popCount)
                throw new InvalidOperationException(
                    "Cannot transition because the page count is less than the pop count.");

            if (IsInTransition)
                throw new InvalidOperationException(
                    "Cannot transition because the screen is already in transition.");

            IsInTransition = true;

            var exitPage = _pages[_pages.Count - 1];
            var unusedPages = new List<Page>();
            for (var i = _pages.Count - 1; i >= _pages.Count - popCount; i--)
                unusedPages.Add(_pages[i]);

            var enterPageIndex = _pages.Count - popCount - 1;
            var enterPage = enterPageIndex < 0 ? null : _pages[enterPageIndex];

            // Preprocess
            foreach (var callbackReceiver in _callbackReceivers)
                callbackReceiver.BeforePop(enterPage, exitPage);

            var preprocessHandles = new List<AsyncProcessHandle>
            {
                exitPage.BeforeExit(false, enterPage)
            };
            if (enterPage != null)
                preprocessHandles.Add(enterPage.BeforeEnter(false, exitPage));

            foreach (var coroutineHandle in preprocessHandles)
                while (!coroutineHandle.IsTerminated)
                    yield return coroutineHandle;

            // Play Animations
            var animationHandles = new List<AsyncProcessHandle>
            {
                exitPage.Exit(false, playAnimation, enterPage)
            };
            if (enterPage != null)
                animationHandles.Add(enterPage.Enter(false, playAnimation, exitPage));

            foreach (var coroutineHandle in animationHandles)
                while (!coroutineHandle.IsTerminated)
                    yield return coroutineHandle;

            // End Transition
            for (var i = 0; i < popCount; i++)
                _pages.RemoveAt(_pages.Count - 1);
            IsInTransition = false;

            // Postprocess
            exitPage.AfterExit(false, enterPage);
            if (enterPage != null)
                enterPage.AfterEnter(false, exitPage);

            foreach (var callbackReceiver in _callbackReceivers)
                callbackReceiver.AfterPop(enterPage, exitPage);

            // Unload Unused Page
            var beforeReleaseHandle = exitPage.BeforeRelease();
            while (!beforeReleaseHandle.IsTerminated)
                yield return null;

            foreach (var unusedPage in unusedPages)
            {
                var unusedPageInstanceId = unusedPage.GetInstanceID();
                var unusedPageId = _instanceIdToPageId[unusedPageInstanceId];
                var loadHandle = _assetLoadHandles[unusedPageId];
                Destroy(unusedPage.gameObject);
                AssetLoader.Release(loadHandle);
                _assetLoadHandles.Remove(unusedPageId);
                _instanceIdToPageId.Remove(unusedPageInstanceId);
            }

            _isActivePageStacked = true;
        }

        public AsyncProcessHandle Preload(string resourceKey, bool loadAsync = true)
        {
            return CoroutineManager.Instance.Run(PreloadRoutine(resourceKey, loadAsync));
        }

        private IEnumerator PreloadRoutine(string resourceKey, bool loadAsync = true)
        {
            if (_preloadedResourceHandles.ContainsKey(resourceKey))
                throw new InvalidOperationException(
                    $"The resource with key \"${resourceKey}\" has already been preloaded.");

            var assetLoadHandle = loadAsync
                ? AssetLoader.LoadAsync<GameObject>(resourceKey)
                : AssetLoader.Load<GameObject>(resourceKey);
            _preloadedResourceHandles.Add(resourceKey, assetLoadHandle);

            if (!assetLoadHandle.IsDone)
                yield return new WaitUntil(() => assetLoadHandle.IsDone);

            if (assetLoadHandle.Status == AssetLoadStatus.Failed)
                throw assetLoadHandle.OperationException;
        }

        public bool IsPreloadRequested(string resourceKey)
        {
            return _preloadedResourceHandles.ContainsKey(resourceKey);
        }

        public bool IsPreloaded(string resourceKey)
        {
            if (!_preloadedResourceHandles.TryGetValue(resourceKey, out var handle))
                return false;

            return handle.Status == AssetLoadStatus.Success;
        }

        public void ReleasePreloaded(string resourceKey)
        {
            if (!_preloadedResourceHandles.ContainsKey(resourceKey))
                throw new InvalidOperationException($"The resource with key \"${resourceKey}\" is not preloaded.");

            var handle = _preloadedResourceHandles[resourceKey];
            _preloadedResourceHandles.Remove(resourceKey);
            AssetLoader.Release(handle);
        }
    }
}
