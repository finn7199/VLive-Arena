using UnityEngine;
using UnityEngine.UI;
using SFB;
using UniVRM10;
using System.IO;
using System;
using OpenSee;

public class UIManager : MonoBehaviour
{
    [Header("UI Buttons")]
    public Button loadVRMButton;

    public OpenSeeVRMSync openSeeVRMSync;

    private void Start()
    {
        loadVRMButton.onClick.AddListener(OnLoadVRMClicked);
    }

    void OnLoadVRMClicked()
    {
        var extensions = new[] {
            new ExtensionFilter("VRM Files", "vrm"),
        };

        StandaloneFileBrowser.OpenFilePanelAsync("Open VRM Model", "", extensions, false, paths =>
        {
            if (paths.Length > 0 && File.Exists(paths[0]))
            {
                LoadVRM(paths[0]);
            }
        });
    }

    async void LoadVRM(string path)
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            var vrmInstance = await Vrm10.LoadBytesAsync(bytes);

            var model = vrmInstance.gameObject;
            model.transform.position = Vector3.zero;
            openSeeVRMSync.vrmInstance = vrmInstance;
        }
        catch(Exception e)
        {
            Debug.LogError("VRM Import Error: " + e);
        }
        
    }

    void OnQuitClicked()
    {
        Application.Quit();
    }
}