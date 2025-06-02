// Copyright (c) Meta Platforms, Inc. and affiliates.

using System.Collections.Generic;
using Meta.XR.Samples;
using Unity.Sentis;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    [MetaCodeSample("PassthroughCameraApiSamples-MultiObjectDetection")]
    public class SentisInferenceUiManager : MonoBehaviour
    {
        [Header("Placement configuration")]
        [SerializeField] private EnvironmentRayCastSampleManager m_environmentRaycast;
        [SerializeField] private WebCamTextureManager m_webCamTextureManager;
        private PassthroughCameraEye CameraEye => m_webCamTextureManager.Eye;

        [Header("UI display references")]
        [SerializeField] private SentisObjectDetectedUiManager m_detectionCanvas;
        [SerializeField] private RawImage m_displayImage;
        [SerializeField] private Sprite m_boxTexture;
        [SerializeField] private Color m_defaultBoxColor;
        [SerializeField] private Font m_font;
        [SerializeField] private Color m_fontColor;
        [SerializeField] private int m_fontSize = 80;

        [Header("Class-specific box colors")]
        [SerializeField]
        private List<ClassColorPair> m_classColors = new List<ClassColorPair>();
        //{
        //    new ClassColorPair { ClassName = "cylinder", Color = Color.red },
        //    new ClassColorPair { ClassName = "hose", Color = Color.blue },
        //    new ClassColorPair { ClassName = "pin", Color = Color.green },
        //    new ClassColorPair { ClassName = "trigger", Color = Color.yellow }
        //};

        [System.Serializable]
        private struct ClassColorPair
        {
            public string ClassName;
            public Color Color;
        }

        [Space(10)]
        public UnityEvent<int> OnObjectsDetected;

        public List<BoundingBox> BoxDrawn = new();

        private string[] m_labels;
        private List<GameObject> m_boxPool = new();
        private Transform m_displayLocation;

        public struct BoundingBox
        {
            public float CenterX;
            public float CenterY;
            public float Width;
            public float Height;
            public string Label;
            public Vector3? WorldPos;
            public string ClassName;
        }

        #region Unity Functions
        private void Start()
        {
            m_displayLocation = m_displayImage.transform;
            // Log all configured class colors for verification
            Debug.Log("Class colors configured:");
            foreach (var classColor in m_classColors)
            {
                Debug.Log($"Class: {classColor.ClassName}, Color: {classColor.Color}");
            }
        }
        #endregion

        #region Detection Functions
        public void OnObjectDetectionError()
        {
            ClearAnnotations();
            OnObjectsDetected?.Invoke(0);
        }
        #endregion

        #region BoundingBoxes functions
        public void SetLabels(TextAsset labelsAsset)
        {
            m_labels = labelsAsset.text.Split('\n');
            // Clean and log labels to verify parsing
            for (int i = 0; i < m_labels.Length; i++)
            {
                m_labels[i] = m_labels[i].Trim(); // Remove leading/trailing whitespace
                Debug.Log($"Label[{i}]: '{m_labels[i]}' (Length: {m_labels[i].Length})");
            }
        }

        public void SetDetectionCapture(Texture image)
        {
            m_displayImage.texture = image;
            m_detectionCanvas.CapturePosition();
        }

        public void DrawUIBoxes(Tensor<float> output, Tensor<int> labelIDs, float imageWidth, float imageHeight)
        {
            m_detectionCanvas.UpdatePosition();
            ClearAnnotations();

            var displayWidth = m_displayImage.rectTransform.rect.width;
            var displayHeight = m_displayImage.rectTransform.rect.height;

            var scaleX = displayWidth / imageWidth;
            var scaleY = displayHeight / imageHeight;

            var halfWidth = displayWidth / 2;
            var halfHeight = displayHeight / 2;

            var boxesFound = output.shape[0];
            if (boxesFound <= 0)
            {
                OnObjectsDetected?.Invoke(0);
                return;
            }
            var maxBoxes = Mathf.Min(boxesFound, 200);

            OnObjectsDetected?.Invoke(maxBoxes);

            var intrinsics = PassthroughCameraUtils.GetCameraIntrinsics(CameraEye);
            var camRes = intrinsics.Resolution;

            for (var n = 0; n < maxBoxes; n++)
            {
                var centerX = output[n, 0] * scaleX - halfWidth;
                var centerY = output[n, 1] * scaleY - halfHeight;
                var perX = (centerX + halfWidth) / displayWidth;
                var perY = (centerY + halfHeight) / displayHeight;

                // Ensure labelID is valid
                if (labelIDs[n] < 0 || labelIDs[n] >= m_labels.Length)
                {
                    Debug.LogWarning($"Invalid label ID: {labelIDs[n]}, skipping box {n}");
                    continue;
                }

                var classname = m_labels[labelIDs[n]].Trim().Replace(" ", "_");
                Debug.Log($"Drawing box {n}: ClassName = '{classname}' (Length: {classname.Length})");

                var centerPixel = new Vector2Int(Mathf.RoundToInt(perX * camRes.x), Mathf.RoundToInt((1.0f - perY) * camRes.y));
                var ray = PassthroughCameraUtils.ScreenPointToRayInWorld(CameraEye, centerPixel);
                var worldPos = m_environmentRaycast.PlaceGameObjectByScreenPos(ray);

                var box = new BoundingBox
                {
                    CenterX = centerX,
                    CenterY = centerY,
                    ClassName = classname,
                    Width = output[n, 2] * scaleX,
                    Height = output[n, 3] * scaleY,
                    Label = $"Class: {classname}",
                    WorldPos = worldPos,
                };

                BoxDrawn.Add(box);
                DrawBox(box, n);
            }
        }

        private void ClearAnnotations()
        {
            foreach (var box in m_boxPool)
            {
                box?.SetActive(false);
            }
            BoxDrawn.Clear();
        }

        private void DrawBox(BoundingBox box, int id)
        {
            Color boxColor = GetClassColor(box.ClassName);

            GameObject panel;
            if (id < m_boxPool.Count)
            {
                panel = m_boxPool[id];
                if (panel == null)
                {
                    panel = CreateNewBox(boxColor);
                }
                else
                {
                    panel.SetActive(true);
                    panel.GetComponent<Image>().color = boxColor;
                }
            }
            else
            {
                panel = CreateNewBox(boxColor);
            }

            panel.transform.localPosition = new Vector3(box.CenterX, -box.CenterY, box.WorldPos.HasValue ? box.WorldPos.Value.z : 0.0f);
            panel.transform.rotation = Quaternion.LookRotation(panel.transform.position - m_detectionCanvas.GetCapturedCameraPosition());
            var rt = panel.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(box.Width, box.Height);
            var label = panel.GetComponentInChildren<Text>();
            label.text = box.Label;
            label.color = boxColor;
            label.fontSize = 12;
        }

        private Color GetClassColor(string className)
        {
            if (string.IsNullOrEmpty(className))
            {
                //Debug.LogWarning("ClassName is null or empty, using default color");
                return m_defaultBoxColor;
            }

            // Normalize className for comparison
            var normalizedClassName = className.Trim().ToLower();

            foreach (var classColor in m_classColors)
            {
                var normalizedConfigName = classColor.ClassName.Trim().ToLower();
                //Debug.Log($"Checking class: '{classColor.ClassName}' (normalized: '{normalizedConfigName}') against '{className}' (normalized: '{normalizedClassName}')");
                if (normalizedConfigName == normalizedClassName)
                {
                    //Debug.Log($"Match found for class '{className}', using color: {classColor.Color}");
                    return classColor.Color;
                }
            }

            Debug.LogWarning($"No color match for class '{className}', using default color");
            return m_defaultBoxColor;
        }

        private GameObject CreateNewBox(Color color)
        {
            var panel = new GameObject("ObjectBox");
            _ = panel.AddComponent<CanvasRenderer>();
            var img = panel.AddComponent<Image>();
            img.color = color;
            img.sprite = m_boxTexture;
            img.type = Image.Type.Sliced;
            img.fillCenter = false;
            panel.transform.SetParent(m_displayLocation, false);

            var text = new GameObject("ObjectLabel");
            _ = text.AddComponent<CanvasRenderer>();
            text.transform.SetParent(panel.transform, false);
            var txt = text.AddComponent<Text>();
            txt.font = m_font;
            txt.color = color;//m_fontColor;
            txt.fontSize = m_fontSize;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;

            var rt2 = text.GetComponent<RectTransform>();
            rt2.offsetMin = new Vector2(20, rt2.offsetMin.y);
            rt2.offsetMax = new Vector2(0, rt2.offsetMax.y);
            rt2.offsetMin = new Vector2(rt2.offsetMin.x, 0);
            rt2.offsetMax = new Vector2(rt2.offsetMax.x, 30);
            rt2.anchorMin = new Vector2(0, 0);
            rt2.anchorMax = new Vector2(1, 1);

            m_boxPool.Add(panel);
            return panel;
        }
        #endregion
    }
    #region OLDCODE
    //[MetaCodeSample("PassthroughCameraApiSamples-MultiObjectDetection")]
    //public class SentisInferenceUiManager : MonoBehaviour
    //{
    //    [Header("Placement configureation")]
    //    [SerializeField] private EnvironmentRayCastSampleManager m_environmentRaycast;
    //    [SerializeField] private WebCamTextureManager m_webCamTextureManager;
    //    private PassthroughCameraEye CameraEye => m_webCamTextureManager.Eye;

    //    [Header("UI display references")]
    //    [SerializeField] private SentisObjectDetectedUiManager m_detectionCanvas;
    //    [SerializeField] private RawImage m_displayImage;
    //    [SerializeField] private Sprite m_boxTexture;
    //    [SerializeField] private Color m_boxColor;
    //    [SerializeField] private Font m_font;
    //    [SerializeField] private Color m_fontColor;
    //    [SerializeField] private int m_fontSize = 80;
    //    [Space(10)]
    //    public UnityEvent<int> OnObjectsDetected;

    //    public List<BoundingBox> BoxDrawn = new();

    //    private string[] m_labels;
    //    private List<GameObject> m_boxPool = new();
    //    private Transform m_displayLocation;

    //    //bounding box data
    //    public struct BoundingBox
    //    {
    //        public float CenterX;
    //        public float CenterY;
    //        public float Width;
    //        public float Height;
    //        public string Label;
    //        public Vector3? WorldPos;
    //        public string ClassName;
    //    }

    //    #region Unity Functions
    //    private void Start()
    //    {
    //        m_displayLocation = m_displayImage.transform;
    //    }
    //    #endregion

    //    #region Detection Functions
    //    public void OnObjectDetectionError()
    //    {
    //        // Clear current boxes
    //        ClearAnnotations();

    //        // Set obejct found to 0
    //        OnObjectsDetected?.Invoke(0);
    //    }
    //    #endregion

    //    #region BoundingBoxes functions
    //    public void SetLabels(TextAsset labelsAsset)
    //    {
    //        //Parse neural net m_labels
    //        m_labels = labelsAsset.text.Split('\n');
    //    }

    //    public void SetDetectionCapture(Texture image)
    //    {
    //        m_displayImage.texture = image;
    //        m_detectionCanvas.CapturePosition();
    //    }

    //    public void DrawUIBoxes(Tensor<float> output, Tensor<int> labelIDs, float imageWidth, float imageHeight)
    //    {
    //        // Updte canvas position
    //        m_detectionCanvas.UpdatePosition();

    //        // Clear current boxes
    //        ClearAnnotations();

    //        var displayWidth = m_displayImage.rectTransform.rect.width;
    //        var displayHeight = m_displayImage.rectTransform.rect.height;

    //        var scaleX = displayWidth / imageWidth;
    //        var scaleY = displayHeight / imageHeight;

    //        var halfWidth = displayWidth / 2;
    //        var halfHeight = displayHeight / 2;

    //        var boxesFound = output.shape[0];
    //        if (boxesFound <= 0)
    //        {
    //            OnObjectsDetected?.Invoke(0);
    //            return;
    //        }
    //        var maxBoxes = Mathf.Min(boxesFound, 200);

    //        OnObjectsDetected?.Invoke(maxBoxes);

    //        //Get the camera intrinsics
    //        var intrinsics = PassthroughCameraUtils.GetCameraIntrinsics(CameraEye);
    //        var camRes = intrinsics.Resolution;

    //        //Draw the bounding boxes
    //        for (var n = 0; n < maxBoxes; n++)
    //        {
    //            // Get bounding box center coordinates
    //            var centerX = output[n, 0] * scaleX - halfWidth;
    //            var centerY = output[n, 1] * scaleY - halfHeight;
    //            var perX = (centerX + halfWidth) / displayWidth;
    //            var perY = (centerY + halfHeight) / displayHeight;

    //            // Get object class name
    //            var classname = m_labels[labelIDs[n]].Replace(" ", "_");

    //            // Get the 3D marker world position using Depth Raycast
    //            var centerPixel = new Vector2Int(Mathf.RoundToInt(perX * camRes.x), Mathf.RoundToInt((1.0f - perY) * camRes.y));
    //            var ray = PassthroughCameraUtils.ScreenPointToRayInWorld(CameraEye, centerPixel);
    //            var worldPos = m_environmentRaycast.PlaceGameObjectByScreenPos(ray);

    //            // Create a new bounding box
    //            var box = new BoundingBox
    //            {
    //                CenterX = centerX,
    //                CenterY = centerY,
    //                ClassName = classname,
    //                Width = output[n, 2] * scaleX,
    //                Height = output[n, 3] * scaleY,
    //                //Label = $"Id: {n} Class: {classname} Center (px): {(int)centerX},{(int)centerY} Center (%): {perX:0.00},{perY:0.00}",
    //                Label = $"Class: {classname}",
    //                WorldPos = worldPos,
    //            };

    //            // Add to the list of boxes
    //            BoxDrawn.Add(box);

    //            // Draw 2D box
    //            DrawBox(box, n);
    //        }
    //    }

    //    private void ClearAnnotations()
    //    {
    //        foreach (var box in m_boxPool)
    //        {
    //            box?.SetActive(false);
    //        }
    //        BoxDrawn.Clear();
    //    }

    //    private void DrawBox(BoundingBox box, int id)
    //    {
    //        //Create the bounding box graphic or get from pool
    //        GameObject panel;
    //        if (id < m_boxPool.Count)
    //        {
    //            panel = m_boxPool[id];
    //            if (panel == null)
    //            {
    //                panel = CreateNewBox(m_boxColor);
    //            }
    //            else
    //            {
    //                panel.SetActive(true);
    //            }
    //        }
    //        else
    //        {
    //            panel = CreateNewBox(m_boxColor);
    //        }
    //        //Set box position
    //        panel.transform.localPosition = new Vector3(box.CenterX, -box.CenterY, box.WorldPos.HasValue ? box.WorldPos.Value.z : 0.0f);
    //        //Set box rotation
    //        panel.transform.rotation = Quaternion.LookRotation(panel.transform.position - m_detectionCanvas.GetCapturedCameraPosition());
    //        //Set box size
    //        var rt = panel.GetComponent<RectTransform>();
    //        rt.sizeDelta = new Vector2(box.Width, box.Height);
    //        //Set label text
    //        var label = panel.GetComponentInChildren<Text>();
    //        label.text = box.Label;
    //        label.fontSize = 12;
    //    }

    //    private GameObject CreateNewBox(Color color)
    //    {
    //        //Create the box and set image
    //        var panel = new GameObject("ObjectBox");
    //        _ = panel.AddComponent<CanvasRenderer>();
    //        var img = panel.AddComponent<Image>();
    //        img.color = color;
    //        img.sprite = m_boxTexture;
    //        img.type = Image.Type.Sliced;
    //        img.fillCenter = false;
    //        panel.transform.SetParent(m_displayLocation, false);

    //        //Create the label
    //        var text = new GameObject("ObjectLabel");
    //        _ = text.AddComponent<CanvasRenderer>();
    //        text.transform.SetParent(panel.transform, false);
    //        var txt = text.AddComponent<Text>();
    //        txt.font = m_font;
    //        txt.color = m_fontColor;
    //        txt.fontSize = m_fontSize;
    //        txt.horizontalOverflow = HorizontalWrapMode.Overflow;

    //        var rt2 = text.GetComponent<RectTransform>();
    //        rt2.offsetMin = new Vector2(20, rt2.offsetMin.y);
    //        rt2.offsetMax = new Vector2(0, rt2.offsetMax.y);
    //        rt2.offsetMin = new Vector2(rt2.offsetMin.x, 0);
    //        rt2.offsetMax = new Vector2(rt2.offsetMax.x, 30);
    //        rt2.anchorMin = new Vector2(0, 0);
    //        rt2.anchorMax = new Vector2(1, 1);

    //        m_boxPool.Add(panel);
    //        return panel;
    //    }
    //    #endregion
    //} 
    #endregion
}
