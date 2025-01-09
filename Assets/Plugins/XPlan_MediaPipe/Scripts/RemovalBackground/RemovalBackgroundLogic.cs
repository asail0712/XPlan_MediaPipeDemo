using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

using Mediapipe;
using Mediapipe.Unity;
using Mediapipe.Unity.Sample;

using XPlan.Observe;
using XPlan.UI;

using TextureFramePool = Mediapipe.Unity.Experimental.TextureFramePool;

namespace XPlan.MediaPipe
{    
    public class RemovalBGMaskMsg : MessageBase
    {
        public float[] maskArray;

        public RemovalBGMaskMsg(float[] maskArray)
        {
            this.maskArray = maskArray;
        }
    }

    public class RemovalBackgroundLogic : LogicComponent
    {
        private RemovalBackgroundGraph graphRunner;
        private RunningMode runningMode;
        private ImageSource imageSource;

        private float[] maskArray;

        public RemovalBackgroundLogic()
        {
            graphRunner = null;
            runningMode = RunningMode.Async;
            imageSource = null;

            RegisterNotify<GraphRunnerPrepareMsg>((msg) => 
            {
                runningMode = msg.runningMode;
                graphRunner = (RemovalBackgroundGraph)msg.graphRunner;

                StartRun();
            });

            RegisterNotify<TexturePrepareMsg>((msg) =>
            {
                imageSource = msg.imageSource;
                maskArray   = new float[imageSource.textureWidth * imageSource.textureHeight];

                StartRun();
            });
        }

        private void StartRun()
        {
            if(graphRunner == null || imageSource == null)
            {
                return;
            }

            StartCoroutine(Run(graphRunner, runningMode, imageSource));
        }

        protected IEnumerator Run(RemovalBackgroundGraph graphRunner, RunningMode runningMode, ImageSource imageSource)
        {
            // Use RGBA32 as the input format.
            // TODO: When using GpuBuffer, MediaPipe assumes that the input format is BGRA, so the following code must be fixed.
            TextureFramePool textureFramePool = new TextureFramePool(imageSource.textureWidth, imageSource.textureHeight, TextureFormat.RGBA32, 10);

            if (!runningMode.IsSynchronous())
            {
                graphRunner.OnSegmentationMaskOutput += OnSegmentationMaskOutput;
            }

            graphRunner.StartRun(imageSource);

            AsyncGPUReadbackRequest req = default;
            WaitUntil waitUntilReqDone  = new WaitUntil(() => req.done);

            // NOTE: we can share the GL context of the render thread with MediaPipe (for now, only on Android)
            bool bCanUseGpuImage        = graphRunner.configType == GraphRunner.ConfigType.OpenGLES && GpuManager.GpuResources != null;
            using GlContext glContext   = bCanUseGpuImage ? GpuManager.GetGlContext() : null;

            while (true)
            {
                if (!textureFramePool.TryGetTextureFrame(out var textureFrame))
                {
                    yield return new WaitForEndOfFrame();
                    continue;
                }

                // Copy current image to TextureFrame
                if (bCanUseGpuImage)
                {
                    yield return new WaitForEndOfFrame();
                    textureFrame.ReadTextureOnGPU(imageSource.GetCurrentTexture());
                }
                else
                {
                    req = textureFrame.ReadTextureAsync(imageSource.GetCurrentTexture());
                    yield return waitUntilReqDone;

                    if (req.hasError)
                    {
                        Debug.LogError($"Failed to read texture from the image source, exiting...");
                        break;
                    }
                }

                graphRunner.AddTextureFrameToInputStream(textureFrame, glContext);

                if (runningMode.IsSynchronous())
                {
                    Task<RemovalBackgroundResult> task = graphRunner.WaitNextAsync();
                    yield return new WaitUntil(() => task.IsCompleted);

                    RemovalBackgroundResult result = task.Result;
                    ProcessImageFrame(result.segmentationMask);
                    result.segmentationMask?.Dispose();
                }
            }
        }
        private void OnSegmentationMaskOutput(object stream, OutputStream<ImageFrame>.OutputEventArgs eventArgs)
        {
            Packet<ImageFrame> packet   = eventArgs.packet;
            ImageFrame imgFrame         = packet == null ? default : packet.Get();

            ProcessImageFrame(imgFrame);

            imgFrame?.Dispose();
        }

        private void ProcessImageFrame(ImageFrame imgFrame)
        {
            if (imgFrame == null)
            {
                return;
            }
            // 將image frame的資料轉移到maskArray
            bool bResult = imgFrame.TryReadChannelNormalized(0, maskArray);

            if (bResult)
            {
                SendGlobalMsg<RemovalBGMaskMsg>(maskArray);
            }
        }
    }
}
