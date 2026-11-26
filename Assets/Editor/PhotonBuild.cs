using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class PhotonBuild
{
    const string ScenePath="Assets/Scenes/ElevenCity.unity";
    public static void CreateCircuit()
    {
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var city=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/ElevenCity.foxmesh");
        var model=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/BlueFalcon.foxmesh");
        PrefabUtility.InstantiatePrefab(city);
        var vehicle=new GameObject("Blue Falcon");
        var body=(GameObject)PrefabUtility.InstantiatePrefab(model);
        body.transform.SetParent(vehicle.transform,false);
        body.transform.localScale=Vector3.one*.65f;
        var plume=new Material(Shader.Find("Universal Render Pipeline/Unlit")) {color=new Color(.33f,.94f,1)};
        AssetDatabase.CreateAsset(plume,"Assets/Art/Exhaust.mat");
        foreach(int side in new[]{-1,1})
        {
            var nozzle=new GameObject("Boost exhaust");nozzle.transform.SetParent(vehicle.transform,false);
            nozzle.transform.localPosition=new Vector3(side*.62f,.43f,-1.69f);
            var trail=nozzle.AddComponent<TrailRenderer>();
            trail.sharedMaterial=plume;trail.time=.055f;trail.startWidth=.22f;trail.endWidth=.01f;
            trail.minVertexDistance=.08f;trail.emitting=false;
        }
        var camera=new GameObject("Chase camera").AddComponent<Camera>();
        camera.tag="MainCamera";camera.nearClipPlane=.1f;camera.farClipPlane=1600;
        camera.gameObject.AddComponent<AudioListener>();
        var sun=new GameObject("Afternoon sun").AddComponent<Light>();
        sun.type=LightType.Directional;sun.transform.rotation=Quaternion.Euler(48,-38,0);
        sun.intensity=1.55f;sun.shadows=LightShadows.Soft;
        RenderSettings.sun=sun;
        var sky=new Material(Shader.Find("Skybox/Panoramic"));
        sky.SetTexture("_MainTex",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/CitySky.jpg"));
        AssetDatabase.CreateAsset(sky,"Assets/Art/CitySky.mat");
        RenderSettings.skybox=sky;
        RenderSettings.ambientMode=AmbientMode.Trilight;
        RenderSettings.ambientSkyColor=new Color(.71f,.80f,.93f);
        RenderSettings.ambientEquatorColor=new Color(.61f,.66f,.71f);
        RenderSettings.ambientGroundColor=new Color(.42f,.41f,.4f);
        RenderSettings.fog=true;RenderSettings.fogMode=FogMode.Linear;
        RenderSettings.fogColor=new Color(.70f,.83f,.87f);RenderSettings.fogStartDistance=135;RenderSettings.fogEndDistance=680;
        var race=new GameObject("Time Trial").AddComponent<PhotonRace>();
        race.vehicle=vehicle.transform;race.chase=camera;race.route=AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Art/Route.json");
        var circuit=JsonUtility.FromJson<CityCircuit>(race.route.text);
        vehicle.transform.SetPositionAndRotation(new Vector3(circuit.spawn.x,.05f,circuit.spawn.y),Quaternion.Euler(0,circuit.angle*Mathf.Rad2Deg,0));
        camera.transform.position=vehicle.transform.TransformPoint(new Vector3(0,5.65f,-13));
        camera.transform.LookAt(vehicle.transform.TransformPoint(new Vector3(0,1.15f,7)));
        race.gameObject.AddComponent<RaceOverlay>();
        EditorSceneManager.SaveScene(scene,ScenePath);
        EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(ScenePath,true)};
        AssetDatabase.SaveAssets();
    }

    public static void BuildCapturePlayer()
    {
        var options=new BuildPlayerOptions {scenes=new[]{ScenePath},target=BuildTarget.StandaloneOSX,
            locationPathName=Path.Combine(Path.GetTempPath(),"Photon Capture.app"),extraScriptingDefines=new[]{"PHOTON_CAPTURE"}};
        var result=BuildPipeline.BuildPlayer(options);
        if(result.summary.result!=BuildResult.Succeeded) throw new System.Exception(result.summary.result.ToString());
    }

    [MenuItem("Photon Fox/Build macOS")]
    public static void BuildMac()
    {
        Directory.CreateDirectory("Builds");
        var path=System.Environment.GetEnvironmentVariable("PHOTON_BUILD_PATH") ?? "Builds/Photon Fox Racing - Full.app";
        var result=BuildPipeline.BuildPlayer(new[]{ScenePath},path,BuildTarget.StandaloneOSX,BuildOptions.None);
        if(result.summary.result!=BuildResult.Succeeded) throw new System.Exception(result.summary.result.ToString());
    }
}
