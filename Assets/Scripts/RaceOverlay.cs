using UnityEngine;

[RequireComponent(typeof(PhotonRace))]
public sealed class RaceOverlay : MonoBehaviour
{
    PhotonRace race;
    Font font;
    GUIStyle style;
    static readonly Color Navy=new Color(.035f,.09f,.15f,.95f), Aqua=new Color(.39f,.94f,.89f), Muted=new Color(.71f,.81f,.85f);
    Texture2D map;
    Vector2 min,max;
    float mapScale;
    const int MapW=180,MapH=100;
    void Start()
    {
        race=GetComponent<PhotonRace>();
        font=Resources.Load<Font>("Fonts/CircuitSans");
        min=max=race.Circuit.points[0];
        foreach(var p in race.Circuit.points){min=Vector2.Min(min,p);max=Vector2.Max(max,p);}
        mapScale=Mathf.Min((MapW-12)/(max.x-min.x),(MapH-12)/(max.y-min.y));
        map=new Texture2D(MapW,MapH,TextureFormat.RGBA32,false);
        map.SetPixels32(new Color32[MapW*MapH]);
        foreach(var p in race.Circuit.points)
        {
            var q=MapPoint(p);int x=Mathf.RoundToInt(q.x), y=Mathf.RoundToInt(q.y);
            for(int a=-1;a<=1;a++) for(int b=-1;b<=1;b++) map.SetPixel(x+a,y+b,Muted);
        }
        map.Apply();
    }
    Vector2 MapPoint(Vector2 p) => (p-(min+max)*.5f)*mapScale+new Vector2(MapW*.5f,MapH*.5f);
    static void Box(float x,float y,float w,float h,Color color)
    {
        GUI.color=color;GUI.DrawTexture(new Rect(x,y,w,h),Texture2D.whiteTexture);GUI.color=Color.white;
    }
    void Label(float x,float y,float w,float h,string text,int size,Color color,bool bold=false,TextAnchor anchor=TextAnchor.MiddleLeft)
    {
        style.fontSize=size;style.normal.textColor=color;style.fontStyle=bold?FontStyle.Bold:FontStyle.Normal;
        style.alignment=anchor;GUI.Label(new Rect(x,y,w,h),text,style);
    }
    public static string Clock(float t) => $"{(int)t/60:00}:{(int)t%60:00}.{(int)(t*100)%100:00}";
    void StartButton(float x,float y,float w,string text)
    {
        var rect=new Rect(x,y,w,52);
        Box(x,y,w,52,rect.Contains(Event.current.mousePosition)?Color.white:Aqua);
        Label(x+18,y,w-32,52,text,20,Navy,true);
        Label(x,y,w-16,52,"↗",24,Navy,true,TextAnchor.MiddleRight);
        if(GUI.Button(rect,GUIContent.none,GUIStyle.none)) race.StartRace();
    }
    void OnGUI()
    {
        if(map==null) return;
        if(style==null) style=new GUIStyle {font=font,clipping=TextClipping.Clip};
        float scale=Mathf.Min(Screen.width/1100f,Screen.height/640f);
        GUI.matrix=Matrix4x4.Scale(new Vector3(scale,scale,1));
        float w=Screen.width/scale,h=Screen.height/scale;
        var driver=race.Driver;var trial=race.Trial;
        if(race.State==PhotonRace.Mode.Ready)
        {
            float x=38,y=(h-398)/2;
            Box(x,y,372,398,Navy);Box(x,y,372,5,Aqua);
            Label(x+24,y+23,320,32,"PHOTON FOX   /   TIME TRIAL",16,Aqua,true);
            Label(x+22,y+60,330,66,"光子狐飞车",49,Color.white,true);
            Label(x+25,y+135,325,34,"十一城迷你版",25,Color.white,true);
            Label(x+26,y+177,320,28,"蓝隼    /    均衡型     ·     两圈计时",16,Muted);
            Box(x+25,y+225,322,1,new Color(.45f,.62f,.68f,.55f));
            Label(x+25,y+239,330,26,"↑ 油门     ↓ 刹车     ← → 转向",16,Color.white);
            Label(x+25,y+268,330,26,"Shift 漂移     W 短喷     空格 氮气",16,Color.white);
            StartButton(x+25,y+320,322,"开始计时赛    ↵");
            return;
        }
        Box(24,24,236,94,Navy);Box(24,24,4,94,Aqua);
        Label(42,31,190,24,"十一城迷你版   /   计时赛",15,Aqua,true);
        Label(40,55,195,52,Clock(trial.elapsed),36,Color.white,true);
        Box(w-222,24,198,154,Navy);
        Label(w-208,31,170,30,$"LAP    {trial.lap}  /  2",22,Color.white,true);
        GUI.DrawTexture(new Rect(w-213,68,MapW,MapH),map);
        var marker=MapPoint(driver.position);
        Box(w-217+marker.x,64+MapH-marker.y,8,8,Aqua);
        Box(w-278,h-132,254,108,Navy);
        Label(w-260,h-128,135,62,Mathf.RoundToInt(driver.speed*3.6f).ToString(),48,Color.white,true);
        Label(w-118,h-106,80,30,"KM/H",18,Muted,true);
        Box(w-258,h-57,214,6,new Color(.20f,.33f,.38f));Box(w-258,h-57,214*driver.charge,6,Aqua);
        Label(w-258,h-49,205,23,$"氮气  {driver.stock} / 2       短喷  {driver.miniCharges}",14,Aqua,true);
        Box(24,h-58,246,34,Navy);Label(36,h-58,225,34,"R  回正      /      Esc  返回发车区",14,Muted);
        if(race.State==PhotonRace.Mode.Countdown)
        {
            Box(w/2-48,h*.31f,96,94,Navy);
            Label(w/2-48,h*.31f,96,94,Mathf.CeilToInt(race.Countdown).ToString(),64,Aqua,true,TextAnchor.MiddleCenter);
        }
        if(race.State==PhotonRace.Mode.Racing && (race.Notice!=null || driver.drifting))
        {
            Box(w/2-117,h*.21f,234,40,Navy);
            Label(w/2-117,h*.21f,234,40,race.Notice??"漂移中",19,Aqua,true,TextAnchor.MiddleCenter);
        }
        if(race.State==PhotonRace.Mode.Results)
        {
            float x=w/2-188,y=h/2-165;Box(x,y,376,330,Navy);Box(x,y,376,5,Aqua);
            Label(x+24,y+20,328,34,"完赛   /   FINISH",20,Aqua,true);
            Label(x+22,y+59,328,65,Clock(trial.elapsed),46,Color.white,true);
            for(int i=0;i<2;i++)
                Label(x+26,y+139+34*i,328,30,$"第 {i+1} 圈          {Clock(trial.splits[i])}",20,Muted);
            StartButton(x+24,y+248,328,"再跑一场    ↵");
        }
    }
}
