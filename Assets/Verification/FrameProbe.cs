#if PHOTON_CAPTURE
using System.Collections;
using System.IO;
using UnityEngine;

// Only compiled into the visual-check player. Observe normal player input
// and record the frame after IMGUI has drawn the HUD.
[DefaultExecutionOrder(-100)]
public sealed class FrameProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod]
    static void Attach() => new GameObject("Frame probe").AddComponent<FrameProbe>();
    bool replay;
    PhotonRace race;
    readonly System.Reflection.FieldInfo inputField=typeof(PhotonRace).GetField("controls",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
    void FixedUpdate()
    {
        if (!replay || race.State!=PhotonRace.Mode.Racing) return;
        var car=race.Driver; var circuit=race.Circuit;
        int i=circuit.Nearest(car.position,out _,out _);
        int n=circuit.points.Length; var target=circuit.points[(i+7)%n]-car.position;
        float angle=FalconDrive.Delta(Mathf.Atan2(target.x,target.y),car.heading);
        float bend=Vector2.Angle(circuit.Tangent(i),circuit.Tangent((i+15)%n));
        float limit=bend>22?12:bend>10?17:28;
        inputField.SetValue(race,new DriveInput {turn=Mathf.Abs(angle)<.012f?0:(int)Mathf.Sign(angle),gas=car.speed<limit,brake=car.speed>limit+1});
    }
    IEnumerator Start()
    {
        race=FindFirstObjectByType<PhotonRace>();
        yield return new WaitForSeconds(.7f);
        yield return Frame("grid");
        yield return new WaitUntil(()=>race.State==PhotonRace.Mode.Countdown);
        yield return Frame("countdown");
        yield return new WaitUntil(()=>race.Driver.speed>15);
        yield return Frame("driving");
        yield return new WaitUntil(()=>race.Driver.drifting);
        yield return Frame("drift");
        race.StartRace(); replay=true; Time.timeScale=6;
        yield return new WaitUntil(()=>race.Trial.previous>.24f && race.Trial.previous<.26f);
        yield return Frame("hairpin");
        yield return new WaitUntil(()=>race.State==PhotonRace.Mode.Results);
        Time.timeScale=1; replay=false; yield return Frame("finish");
        yield return new WaitUntil(()=>race.State==PhotonRace.Mode.Countdown);
        Debug.Log("Verified restart: lap "+race.Trial.lap+" time "+race.Trial.elapsed);
    }
    static IEnumerator Frame(string name)
    {
        yield return new WaitForEndOfFrame();
        var tex=new Texture2D(Screen.width,Screen.height,TextureFormat.RGB24,false);
        tex.ReadPixels(new Rect(0,0,Screen.width,Screen.height),0,0);
        tex.Apply();
        using (var file=File.Create(Path.Combine(Path.GetTempPath(),"photon-"+name+".ppm")))
        {
            var header=System.Text.Encoding.ASCII.GetBytes($"P6\n{tex.width} {tex.height}\n255\n");
            file.Write(header,0,header.Length);
            var rgb=tex.GetRawTextureData();
            for (int y=tex.height-1;y>=0;y--) file.Write(rgb,y*tex.width*3,tex.width*3);
        }
        Destroy(tex);
        Debug.Log("Captured "+name);
    }
}
#endif
