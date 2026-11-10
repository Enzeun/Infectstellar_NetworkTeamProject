using MoreMountains.Tools;
using System.Collections;
using System.Diagnostics;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;
using Photon.Pun;
using System.Threading;
using System;

namespace ENZEUN.Runtime
{

    public class LoadingHelper : MMAdditiveSceneLoadingManager
    {
        private PhotonLinkManager photonLinkManager;
        private CancellationTokenSource _cts;
        private bool _isCancelRequested = false;

        private void Start()
        {
            if (!PhotonNetwork.IsConnected)
            {
                return;
            }

            PhotonNetwork.AutomaticallySyncScene = false;

            if (PhotonLinkManager.Instance == null)
            {
                MMLoadingSceneDebug("포톤 연결이 끊겼습니다. 심각한 에러 입니다");
            }
            else
            {
                photonLinkManager = PhotonLinkManager.Instance;

                photonLinkManager.OnAllPlayerReadyToNextScene -= OnReady;
                photonLinkManager.OnAllPlayerReadyToNextScene += OnReady;
            }

            _cts = new CancellationTokenSource();

            CheckTime(_cts.Token).Forget();
        }

        private void OnDisable()
        {
            if (photonLinkManager != null)
            {
                photonLinkManager.OnAllPlayerReadyToNextScene -= OnReady;
            }

            _cts?.Cancel();
            _cts?.Dispose();
        }

        public void SetCancelCondition(bool cancel)
        {
            _isCancelRequested = cancel;
            if (_isCancelRequested)
            {
                _cts?.Cancel(); // 조건이 true가 되면 즉시 취소 요청
            }
        }

        private void OnReady()
        {
            SetCancelCondition(true);

            ActivateScene();
        }

        public void ActivateScene()
        {
            if (_loadDestinationAsyncOperation != null)
            {
                _loadDestinationAsyncOperation.allowSceneActivation = true;
            }
        }

        protected override IEnumerator DestinationSceneActivation()
        {
            yield return MMCoroutine.WaitForFrames(1);
            //_loadDestinationAsyncOperation.allowSceneActivation = true;

            // 포톤에 연결되어 있을 때.
            if (PhotonNetwork.InRoom)
            {
                bool loadSceneUpdated = false;

                while (_loadDestinationAsyncOperation.progress < 1.0f)
                {
                    if (!loadSceneUpdated)
                    {
                        if (_loadDestinationAsyncOperation.progress >= 0.9f)
                        {
                            loadSceneUpdated = true;
                            photonLinkManager?.SetLoadSceneState(true);
                        }
                    }

                    yield return null;
                }
            }
            // 포톤 연결이 아닐 때
            else
            {
                _loadDestinationAsyncOperation.allowSceneActivation = true;

                while (_loadDestinationAsyncOperation.progress < 1.0f)
                {
                    yield return null;
                }
            }

            MMLoadingSceneDebug("MMLoadingSceneManagerAdditive : activating destination scene");
            MMSceneLoadingManager.LoadingSceneEvent.Trigger(_sceneToLoadName, MMSceneLoadingManager.LoadingStatus.DestinationSceneActivation);

            OnDestinationSceneActivation?.Invoke();
        }

        private async UniTaskVoid CheckTime(CancellationToken externalToken)
        {
            var destroyToken = this.GetCancellationTokenOnDestroy();
            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(destroyToken, externalToken);

            try
            {
                // 120초 안에 전원 로딩 완료(취소 요청)가 오지 않으면 Timeout 이 TimeoutException 을 던집니다.
                await UniTask.WaitUntil(() => _isCancelRequested, cancellationToken: linkedCts.Token)
                    .Timeout(TimeSpan.FromSeconds(120));
            }
            catch (TimeoutException)
            {
                MMLoadingSceneDebug("시간이 초과되었습니다. 메인화면으로 돌아갑니다.");
                SceneManager.LoadScene("TitleScene");
            }
            catch (OperationCanceledException)
            {
                MMLoadingSceneDebug("CheckTime 작업이 정상적으로 취소되었습니다.");
            }
            finally
            {
                // 시간 초과 후에도 WaitUntil 이 계속 돌지 않도록 취소한 뒤 해제합니다.
                linkedCts.Cancel();
                linkedCts.Dispose();
            }
        }
    }
}