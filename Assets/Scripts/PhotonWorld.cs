#if (UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX) && !PHOTON_CAPTURE
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class PhotonWorld : MonoBehaviour
{
    [Serializable] class LightingTable { public Lighting[] maps; }
    [Serializable] class Lighting
    {
        public string id, sun, sky, ground, fog;
        public float azimuth, elevation, intensity, ambient;
    }
    sealed class Racer
    {
        public GameObject root;
        public string vehicle, paint;
        public TrailRenderer[] trails;
        public TrailRenderer[] sparks;
    }

    readonly Dictionary<string,Racer> racers = new Dictionary<string,Racer>();
    readonly Dictionary<string,GameObject> effects = new Dictionary<string,GameObject>();
    readonly HashSet<string> visibleRacers = new HashSet<string>();
    readonly HashSet<string> visibleEffects = new HashSet<string>();
    readonly List<string> staleObjects = new List<string>();
    readonly List<Material> materials = new List<Material>();
    const int SkidChunk = 2400, MaxSkidChunks = 24;
    readonly Queue<Mesh> marks = new Queue<Mesh>();
    readonly List<Vector3> markVertices = new List<Vector3>(SkidChunk);
    readonly List<int> markIndices = new List<int>(SkidChunk);
    Mesh activeMarks;
    Transform markRoot;
    Camera chase;
    Light lightSource;
    LightingTable lighting;
    GameObject activeTrack;
    string map;
    Material sky, skid, cyan, gold, blue, box, banana, shield, violet, smoke, orange;

    static Color Decode(string hex, Color fallback) =>
        ColorUtility.TryParseHtmlString(hex,out var result)?result:fallback;

    public void Initialize(PhotonRace original)
    {
        chase=original.chase;
        chase.farClipPlane=1400;
        lightSource=RenderSettings.sun;
        lighting=JsonUtility.FromJson<LightingTable>(Resources.Load<TextAsset>("Photon/lighting").text);
        sky=new Material(Shader.Find("Skybox/Panoramic"));
        materials.Add(sky);
        RenderSettings.ambientMode=AmbientMode.Trilight;
        RenderSettings.fog=true;
        RenderSettings.fogMode=FogMode.Linear;
        RenderSettings.fogStartDistance=280;
        RenderSettings.fogEndDistance=950;
        skid=Unlit(new Color(.09f,.14f,.18f,.55f),true);
        cyan=Unlit(new Color(.22f,.88f,1));
        gold=Unlit(new Color(1,.81f,.23f));
        blue=Unlit(new Color(.35f,.58f,1));
        violet=Unlit(new Color(.65f,.57f,1));
        smoke=Unlit(new Color(.85f,.94f,1,.52f),true);
        orange=Unlit(new Color(1,.53f,.18f,.8f),true);
        shield=Unlit(new Color(.32f,.88f,1,.18f),true);
        box=Unlit(Color.white,true,"item-box");
        banana=Unlit(Color.white,true,"item-banana");
        markRoot=new GameObject("Tire marks").transform;
        markRoot.SetParent(transform,false);
        var track=GameObject.Find("ElevenCity");
        if (track!=null) track.SetActive(false);
        original.vehicle.gameObject.SetActive(false);
        original.gameObject.SetActive(false);
        SelectMap("city");
    }

    Material Unlit(Color color, bool alpha=false, string tex=null)
    {
        var material=new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        material.SetColor("_BaseColor",color);
        material.SetInt("_Cull",(int)CullMode.Off);
        if (tex!=null) material.SetTexture("_BaseMap",Resources.Load<Texture2D>("Photon/Textures/"+tex));
        if(alpha)
        {
            material.SetFloat("_Surface",1);
            material.SetFloat("_SrcBlend",(int)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend",(int)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite",0);
            material.renderQueue=(int)RenderQueue.Transparent;
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }
        materials.Add(material);
        return material;
    }

    void SelectMap(string next)
    {
        if (next==map) return;
        var prefab=Resources.Load<GameObject>("Photon/Tracks/"+next);
        if (prefab==null) throw new InvalidOperationException($"Missing Photon map {next}");
        if (activeTrack!=null) {activeTrack.SetActive(false);Destroy(activeTrack);}
        activeTrack=Instantiate(prefab,transform);
        var config=Array.Find(lighting.maps,entry=>entry.id==next);
        if (config!=null)
        {
            float a=config.azimuth*Mathf.Deg2Rad, e=config.elevation*Mathf.Deg2Rad;
            lightSource.transform.forward=-new Vector3(Mathf.Sin(a)*Mathf.Cos(e),Mathf.Sin(e),-Mathf.Cos(a)*Mathf.Cos(e));
            lightSource.color=Decode(config.sun,Color.white);
            lightSource.intensity=config.intensity*.71f;
            RenderSettings.ambientSkyColor=Decode(config.sky,Color.white)*(.52f+config.ambient*.35f);
            RenderSettings.ambientGroundColor=Decode(config.ground,Color.gray);
            RenderSettings.ambientEquatorColor=Color.Lerp(RenderSettings.ambientSkyColor,RenderSettings.ambientGroundColor,.45f);
            RenderSettings.fogColor=Decode(config.fog,new Color(.72f,.86f,.91f));
        }
        bool skyless = next=="snow";
        var texture=Resources.Load<Texture2D>("Photon/Textures/sky-"+(next=="city-full"?"city":next));
        if (texture==null && !skyless) texture=Resources.Load<Texture2D>("Photon/Textures/sky-city");
        if (texture!=null)
        {
            sky.SetTexture("_MainTex",texture);
            sky.SetFloat("_Rotation",next=="city"?-57:next=="city-full"?120:next=="akina"?-46:0);
            RenderSettings.skybox=sky;
            chase.clearFlags=CameraClearFlags.Skybox;
        }
        else
        {
            RenderSettings.skybox=null;
            chase.backgroundColor=Decode("#b4dce9",Color.cyan);
            chase.clearFlags=CameraClearFlags.SolidColor;
        }
        map=next;
        ClearMarks();
    }

    public void Apply(PhotonDesktop.Message frame)
    {
        SelectMap(frame.map);
        chase.transform.SetPositionAndRotation(frame.camera.Position,frame.camera.Rotation);
        chase.fieldOfView=frame.fov;
        bool playing=frame.mode=="racing";
        visibleRacers.Clear();
        foreach (var pose in frame.cars)
        {
            visibleRacers.Add(pose.id);
            if (!racers.TryGetValue(pose.id,out var racer) || racer.vehicle!=pose.vehicle)
            {
                if(racer!=null) Destroy(racer.root);
                racer=CreateRacer(pose.vehicle);
                racers[pose.id]=racer;
            }
            if(racer.paint!=pose.color) SetPaint(racer,pose.color);
            racer.root.SetActive(pose.visible);
            racer.root.transform.SetPositionAndRotation(pose.Position,pose.Rotation);
            racer.root.transform.localScale=Vector3.one*pose.scale;
            foreach(var trail in racer.trails)
            {
                trail.emitting=pose.boost>0 && playing && pose.visible;
                trail.sharedMaterial=pose.boost==3?gold:pose.boost==2?cyan:blue;
            }
            foreach(var spark in racer.sparks)
                spark.emitting=playing && pose.id=="local" && frame.drifting;
        }
        RemoveMissing(racers,visibleRacers,r=>r.root);
        if (frame.clearMarks) ClearMarks();
        AppendMarks(frame.marks);
        visibleEffects.Clear();
        foreach(var fx in frame.effects)
        {
            visibleEffects.Add(fx.id);
            if (!effects.TryGetValue(fx.id,out var obj)) effects[fx.id]=obj=CreateEffect(fx.type);
            obj.transform.SetPositionAndRotation(new Vector3(fx.x,fx.y,fx.z),EffectRotation(fx));
        }
        RemoveMissing(effects,visibleEffects,e=>e);
    }

    void RemoveMissing<T>(Dictionary<string,T> current, HashSet<string> live, Func<T,GameObject> get)
    {
        staleObjects.Clear();
        foreach(var entry in current) if(!live.Contains(entry.Key)) staleObjects.Add(entry.Key);
        foreach(var id in staleObjects)
        {
            var stale=get(current[id]);
            stale.SetActive(false);
            Destroy(stale);
            current.Remove(id);
        }
    }

    Racer CreateRacer(string vehicle)
    {
        var prefab=Resources.Load<GameObject>("Photon/Cars/"+vehicle);
        var parent=new GameObject(vehicle);
        parent.transform.SetParent(transform,false);
        Instantiate(prefab,parent.transform);
        var trails=new TrailRenderer[2];
        for(int i=0;i<2;i++)
        {
            var nozzle=new GameObject("Exhaust");
            nozzle.transform.SetParent(parent.transform,false);
            nozzle.transform.localPosition=new Vector3(i==0?-.95f:.95f,.65f,-2.6f);
            var trail=nozzle.AddComponent<TrailRenderer>();
            trail.sharedMaterial=cyan;
            trail.time=.12f;
            trail.startWidth=.36f;
            trail.endWidth=.01f;
            trail.minVertexDistance=.10f;
            trail.numCornerVertices=3;
            trail.shadowCastingMode=ShadowCastingMode.Off;
            trail.emitting=false;
            trails[i]=trail;
        }
        var sparks=new TrailRenderer[2];
        for(int i=0;i<2;i++)
        {
            var tire=new GameObject("Drift sparks");
            tire.transform.SetParent(parent.transform,false);
            tire.transform.localPosition=new Vector3(i==0?-1.65f:1.65f,.07f,-1.8f);
            var spark=tire.AddComponent<TrailRenderer>();
            spark.sharedMaterial=gold;
            spark.time=.042f;
            spark.startWidth=.18f;
            spark.endWidth=0;
            spark.minVertexDistance=.055f;
            spark.shadowCastingMode=ShadowCastingMode.Off;
            spark.emitting=false;
            sparks[i]=spark;
        }
        return new Racer{root=parent,trails=trails,sparks=sparks,vehicle=vehicle};
    }

    static void SetPaint(Racer racer,string hex)
    {
        racer.paint=hex;
        var block=new MaterialPropertyBlock();
        block.SetColor("_BaseColor",Decode(hex,Decode("#e9f6ff",Color.white)));
        foreach(var renderer in racer.root.GetComponentsInChildren<MeshRenderer>())
            if(renderer.sharedMaterial.name=="Driver identity") renderer.SetPropertyBlock(block);
    }

    void ClearMarks()
    {
        foreach(var mesh in marks) Destroy(mesh);
        foreach(Transform part in markRoot) {part.gameObject.SetActive(false);Destroy(part.gameObject);}
        marks.Clear();
        activeMarks=null;
        markVertices.Clear();
        markIndices.Clear();
    }

    void AppendMarks(float[] vertices)
    {
        if(vertices==null) return;
        for(int i=0;i<vertices.Length;i+=3)
        {
            if(activeMarks==null)
            {
                if(marks.Count>=MaxSkidChunks)
                {
                    var old=marks.Dequeue();
                    var oldPart=markRoot.GetChild(0);
                    oldPart.SetParent(null);
                    Destroy(oldPart.gameObject);
                    Destroy(old);
                }
                activeMarks=new Mesh{name="Drift tire marks"};
                activeMarks.MarkDynamic();
                marks.Enqueue(activeMarks);
                var part=new GameObject("Tire marks section");
                part.transform.SetParent(markRoot,false);
                part.AddComponent<MeshFilter>().sharedMesh=activeMarks;
                part.AddComponent<MeshRenderer>().sharedMaterial=skid;
            }
            markIndices.Add(markVertices.Count);
            markVertices.Add(new Vector3(vertices[i],vertices[i+1],-vertices[i+2]));
            if (markVertices.Count==SkidChunk || i+3==vertices.Length)
            {
                activeMarks.SetVertices(markVertices);
                activeMarks.SetTriangles(markIndices,0,true);
            }
            if(markVertices.Count!=SkidChunk) continue;
            activeMarks=null;
            markIndices.Clear();
            markVertices.Clear();
        }
    }

    GameObject CreateEffect(string type)
    {
        var primitive=type=="box"||type=="banana"?PrimitiveType.Quad:PrimitiveType.Sphere;
        var obj=GameObject.CreatePrimitive(primitive);
        obj.name=type;
        Destroy(obj.GetComponent<Collider>());
        obj.transform.SetParent(transform,false);
        var renderer=obj.GetComponent<MeshRenderer>();
        renderer.shadowCastingMode=ShadowCastingMode.Off;
        renderer.sharedMaterial=type switch
        {
            "box"=>box, "banana"=>banana, "shield"=>shield, "cloud"=>smoke,
            "projectile"=>orange, "impact"=>orange, _=>violet
        };
        obj.transform.localScale=type switch
        {
            "box"=>Vector3.one*2.7f, "banana"=>Vector3.one*2.5f, "projectile"=>Vector3.one*1.3f,
            "shield"=>Vector3.one*6.8f, "trap"=>Vector3.one*6, "cloud"=>Vector3.one*12,
            "ufo"=>new Vector3(4,1,4), "tornado"=>new Vector3(9,12,9), _=>new Vector3(5.6f,.4f,5.6f)
        };
        return obj;
    }

    static Quaternion EffectRotation(PhotonDesktop.Effect effect) =>
        Quaternion.Euler(effect.type=="banana"?90:0,effect.yaw*Mathf.Rad2Deg,0);

    void OnDestroy()
    {
        foreach(var mesh in marks) Destroy(mesh);
        foreach(var material in materials) Destroy(material);
    }
}
#endif
