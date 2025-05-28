using PassthroughCameraSamples;
using UnityEngine;

public class PassthroughCropCamera : MonoBehaviour
{
    public FireExDetection fireExDetection;
    public float cropPercent;
    public WebCamTextureManager webCamManager;

    public Renderer quadRenderer;
    public float quadDistance = 1f;


    private Texture2D picture;
    private RenderTexture webCamRendertexture;

    // Update is called once per frame
    void Update()
    {
        if (!webCamManager.WebCamTexture)
        {
            return;
        }

        PlaceQuad();
        Takepicture();

        int result = fireExDetection.RunAI(picture);
    }

    public void Takepicture()
    {
        int sourceWidth = webCamManager.WebCamTexture.width;
        int sourceHeight = webCamManager.WebCamTexture.height;

        int cropWidth = Mathf.RoundToInt(sourceWidth * cropPercent);

        int startX = (sourceWidth - cropWidth) / 2;
        int startY = (sourceHeight - cropWidth) / 2;

        if (webCamRendertexture == null)
        {
            webCamRendertexture = new RenderTexture(sourceWidth, sourceHeight, 0);
        }

        // Blit (copy) webcam texture into the render texture
        Graphics.Blit(webCamManager.WebCamTexture, webCamRendertexture);

        // Create a new texture with the cropped size
        if (picture == null || picture.width != cropWidth || picture.height != cropWidth)
        {
            picture = new Texture2D(cropWidth, cropWidth, TextureFormat.RGBA32, false);
        }

        // Read pixels from the render texture
        RenderTexture.active = webCamRendertexture;

        //Note: y axis is flipped in readpixels
        picture.ReadPixels(new Rect(startX, sourceHeight - startY - cropWidth, cropWidth, cropWidth), 0, 0);
        picture.Apply();


    }


    public void PlaceQuad()
    {
        Transform quadtransform = quadRenderer.transform;

        Pose cameraPose = PassthroughCameraUtils.GetCameraPoseInWorld(PassthroughCameraEye.Left);

        Vector2Int resolution = PassthroughCameraUtils.GetCameraIntrinsics(PassthroughCameraEye.Left).Resolution;

        int width = (int)(resolution.x * cropPercent);

        quadtransform.position = cameraPose.position + cameraPose.forward * quadDistance;
        quadtransform.rotation = cameraPose.rotation;

        Ray leftSideRay = PassthroughCameraUtils.ScreenPointToRayInCamera(PassthroughCameraEye.Left, new Vector2Int((resolution.x - width) / 2, resolution.y / 2));
        Ray rightSideRay = PassthroughCameraUtils.ScreenPointToRayInCamera(PassthroughCameraEye.Left, new Vector2Int((resolution.x + width) / 2, resolution.y / 2));

        float horizontalFov = Vector3.Angle(leftSideRay.direction, rightSideRay.direction);

        float quadScale = 2 * quadDistance * Mathf.Tan(horizontalFov * Mathf.Deg2Rad / 2);

        quadtransform.localScale = new Vector3(quadScale, quadScale, 1f);
    }
}
