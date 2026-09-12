using Unity;
using System.Collections.Generic;
using UnityEngine;
using Unity.Barracuda;
using System;
using System.Linq;
using static MoodMe.FaceDetectorPostProcessing;
namespace MoodMe
{
    public class ManageEmotionsNetwork : MonoBehaviour
    {
        private const int SourceCropWidth = 48;
        private const int SourceCropHeight = 48;
        private const int CropCount = 10;

        public NNModel EmotionsNNetwork;

        public int ImageNetworkWidth = 44;
        public int ImageNetworkHeight = 44;

        [Range(1, 4)]
        public int ChannelCount = 3;

        public WorkerFactory.Device Device = WorkerFactory.Device.CPU;

        public bool Process;

        public GameObject PreviewEmotionsPlane;

        public float[] GetCurrentEmotionValues {
            get {
                    return EmotionsLabelFull.Select(label => DetectedEmotions[label]).ToArray();
                }
        }


        private static Dictionary<string,float> DetectedEmotions;

        //private static MoodMeEmotions.MDMEmotions CurrentEmotions;

        private IWorker engine;

        private string[] EmotionsLabelFull = { "Angry", "Disgusted", "Scared", "Happy", "Sad", "Surprised", "Neutral" };
        private string[] EmotionsLabel = { "Angry", "Disgusted", "Scared", "Happy", "Sad", "Surprised", "Neutral" };


        private Tensor tensor;
        private Tensor output;
        private bool resourcesReleased;

        private Color32[] rgba;
        private float[] tensorData;

        // Start is called before the first frame update
        void Start()
        {

            Model model = ModelLoader.Load(EmotionsNNetwork);
            engine = WorkerFactory.CreateWorker(model, Device);

            DetectedEmotions = new Dictionary<string, float>();

            foreach(string key in EmotionsLabelFull)
            {
                DetectedEmotions.Add(key, 0);
            }
            
        }

        // Update is called once per frame
        Texture2D previewTexture;
        void Update()
        {
            if (!Process) return;
            if (resourcesReleased || engine == null) return;
            if (FaceDetector.OutputCrop == null) return;
            if (!(FaceDetector.OutputCrop.Length == (SourceCropWidth * SourceCropHeight))) return;
            previewTexture = new Texture2D(SourceCropWidth, SourceCropHeight, TextureFormat.RGBA32, false);


            rgba = FaceDetector.OutputCrop;

            previewTexture.SetPixels32(rgba);
            previewTexture.Apply();


            if (PreviewEmotionsPlane != null) PreviewEmotionsPlane.GetComponent<MeshRenderer>().material.mainTexture = previewTexture;

            Texture2D[] crops = ComputeInputTensor(previewTexture);
            tensor = new Tensor(crops, ChannelCount);
            DateTime timestamp;

            timestamp = DateTime.Now;
            output = engine.ExecuteAndWaitForCompletion(tensor);
            //Debug.Log("EMOTIONS INFERENCE TIME: " + (DateTime.Now - timestamp).TotalMilliseconds + " ms");
            float[] results = output.data.Download(output.shape);
            float[] results_avg = AverageCropResults(results, CropCount, EmotionsLabel.Length);
            float[] scores = Softmax(results_avg);

            for (int i = 0; i < EmotionsLabel.Length; i++)
            {
                DetectedEmotions[EmotionsLabel[i]] = scores[i];
                //Debug.Log(EmotionsLabel[i] + " = " + scores[i]);
            }
            //Debug.Log("-------------------------------------------");
            DisposeTextures(crops);
            DisposeTexture(ref previewTexture);
            DisposeInferenceTensors();
            Process = false;
        }
        private Texture2D[] ComputeInputTensor(Texture2D source)
        {
            Vector2Int[] offsets = {
                new Vector2Int(0, 0),
                new Vector2Int(0, SourceCropHeight - ImageNetworkHeight),
                new Vector2Int(SourceCropWidth - ImageNetworkWidth, 0),
                new Vector2Int(SourceCropWidth - ImageNetworkWidth, SourceCropHeight - ImageNetworkHeight),
                new Vector2Int((SourceCropWidth - ImageNetworkWidth) / 2, (SourceCropHeight - ImageNetworkHeight) / 2)
            };

            Texture2D[] textures = new Texture2D[CropCount];
            for (int i = 0; i < offsets.Length; i++)
            {
                Color[] pixels = ToGrayscaleRgb(source.GetPixels(offsets[i].x, offsets[i].y, ImageNetworkWidth, ImageNetworkHeight), false);
                textures[i] = CreateCropTexture(pixels);
                textures[i + offsets.Length] = CreateCropTexture(ToGrayscaleRgb(source.GetPixels(offsets[i].x, offsets[i].y, ImageNetworkWidth, ImageNetworkHeight), true));
            }

            return textures;
        }

        private Texture2D CreateCropTexture(Color[] pixels)
        {
            Texture2D texture = new Texture2D(ImageNetworkWidth, ImageNetworkHeight, TextureFormat.RGB24, false);
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private Color[] ToGrayscaleRgb(Color[] pixels, bool horizontalFlip)
        {
            Color[] outputPixels = new Color[pixels.Length];
            for (int y = 0; y < ImageNetworkHeight; y++)
            {
                for (int x = 0; x < ImageNetworkWidth; x++)
                {
                    int sourceX = horizontalFlip ? ImageNetworkWidth - 1 - x : x;
                    Color pixel = pixels[sourceX + y * ImageNetworkWidth];
                    float gray = pixel.r * 0.299f + pixel.g * 0.587f + pixel.b * 0.114f;
                    outputPixels[x + y * ImageNetworkWidth] = new Color(gray, gray, gray, 1f);
                }
            }

            return outputPixels;
        }

        private float[] AverageCropResults(float[] results, int cropCount, int classCount)
        {
            float[] averaged = new float[classCount];
            if (results.Length < cropCount * classCount) return averaged;

            for (int crop = 0; crop < cropCount; crop++)
            {
                for (int label = 0; label < classCount; label++)
                {
                    averaged[label] += results[crop * classCount + label];
                }
            }

            for (int label = 0; label < classCount; label++)
            {
                averaged[label] /= cropCount;
            }

            return averaged;
        }

        private float[] Softmax(float[] values)
        {
            float max = values.Max();
            float sum = 0f;
            float[] exp = new float[values.Length];

            for (int i = 0; i < values.Length; i++)
            {
                exp[i] = Mathf.Exp(values[i] - max);
                sum += exp[i];
            }

            if (sum <= 0f) return exp;

            for (int i = 0; i < exp.Length; i++)
            {
                exp[i] /= sum;
            }

            return exp;
        }

        private void DisposeTexture(ref Texture2D texture)
        {
            if (texture != null)
            {
                Destroy(texture);
                texture = null;
            }
        }
        private void DisposeTexture(Texture2D texture)
        {
            if (texture != null)
            {
                Destroy(texture);
            }
        }
        private void DisposeTextures(Texture2D[] textures)
        {
            if (textures == null) return;
            foreach (Texture2D texture in textures)
            {
                DisposeTexture(texture);
            }
        }

        private void DisposeInferenceTensors()
        {
            DisposeTensor(ref output);
            DisposeTensor(ref tensor);
        }

        private void DisposeTensor(ref Tensor tensorToDispose)
        {
            if (tensorToDispose == null) return;

            tensorToDispose.Dispose();
            tensorToDispose = null;
        }

        private void ReleaseResources()
        {
            if (resourcesReleased) return;

            resourcesReleased = true;
            Process = false;
            DisposeInferenceTensors();

            if (engine != null)
            {
                engine.Dispose();
                engine = null;
            }

            DisposeTexture(ref previewTexture);
        }

        void OnApplicationQuit()
        {
            ReleaseResources();
        }

        void OnDestroy()
        {
            ReleaseResources();
        }

       



    }

}
