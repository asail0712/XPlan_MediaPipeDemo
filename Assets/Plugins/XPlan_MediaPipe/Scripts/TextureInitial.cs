using System.Collections;
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
    public class TexturePrepareMsg : MessageBase
    {
        public ImageSource imageSource;

        public TexturePrepareMsg(ImageSource imageSource)
        {
            this.imageSource = imageSource;
        }
    }

    public class TextureInitial : LogicComponent
    {
        public TextureInitial(ImageSourceType type)
        {
            ImageSource imgSource = null;

            switch (type)
            {
                case ImageSourceType.WebCamera:
                    imgSource = new CamTextureSource();
                    break;
                case ImageSourceType.Kinect:
                    imgSource = new KinectTextureSource();
                    break;
            }

            StartCoroutine(Run(imgSource));
        }

        protected IEnumerator Run(ImageSource imageSource)
        {           
            // 等待攝像機初始化
            yield return imageSource.Play();

            if (!imageSource.isPrepared)
            {
                Debug.LogError("Failed to start ImageSource, exiting...");
                yield break;
            }

            // 初始化UI
            UISystem.DirectCall<ImageSource>(UICommand.InitScreen, imageSource);

            // 將初始化的結果送出
            SendMsg<TexturePrepareMsg>(imageSource);
        }
    }
}
