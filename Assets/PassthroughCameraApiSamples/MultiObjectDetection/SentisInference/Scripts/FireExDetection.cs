using Unity.Sentis;
using UnityEngine;

public class FireExDetection : MonoBehaviour
{
    public float threshold = 0.5f;
    public Texture2D testPicture;

    public ModelAsset modelAsset;
    public float[] results;

    private Worker worker;

    #region OLDCODE
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        Model model = ModelLoader.Load(modelAsset);

        FunctionalGraph graph = new FunctionalGraph();
        FunctionalTensor[] inputs = graph.AddInputs(model);
        FunctionalTensor[] outputs = Functional.Forward(model, inputs);

        FunctionalTensor softmax = Functional.Softmax(outputs[0]);

        Model compiledModel = graph.Compile(softmax);

        worker = new Worker(compiledModel, BackendType.GPUCompute);

        Debug.LogError(RunAI(testPicture));
    }

    public int RunAI(Texture2D picture)
    {
        using Tensor<float> inputTensor = TextureConverter.ToTensor(picture, 640, 640, 3);

        worker.Schedule(inputTensor);

        Tensor<float> outputTensor = worker.PeekOutput() as Tensor<float>;

        results = outputTensor.DownloadToArray();

        return GetMaxIndex(results);
    }

    private void OnDisable()
    {
        worker?.Dispose();
    }

    public int GetMaxIndex(float[] array)
    {
        int maxIndex = 0;

        for (int i = 1; i < array.Length; i++)
        {
            if (array[i] > array[maxIndex])
            {
                maxIndex = i;
            }
        }

        Debug.LogError("Max index: " + maxIndex + "value:  " + array[maxIndex]);
        if (array[maxIndex] > threshold)
        {
            return maxIndex;
        }
        else
        {
            return -1;
        }
    }
    #endregion
}
