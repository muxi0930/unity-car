using UnityEngine;
using static UnityEngine.Mathf;

public class PhotonRace : MonoBehaviour
{
    public enum Mode { Ready, Countdown, Racing, Results }
    public Transform vehicle;
    public Camera chase;
    public TextAsset route;
    public CityCircuit Circuit {get;private set;}
    public FalconDrive Driver {get;private set;}
    public TwoLapTrial Trial {get;private set;}
    public Mode State {get;private set;}
    public float Countdown {get;private set;}
    public string Notice {get;private set;}
    float noticeUntil, cameraAngle, cameraRoll, menuTime;
    DriveInput controls;
    TrailRenderer[] exhaust;
    const float StepTime=1f/120;

    void Awake()
    {
        Circuit=JsonUtility.FromJson<CityCircuit>(route.text);
        Time.fixedDeltaTime=StepTime;
        Application.targetFrameRate=60;
        exhaust=vehicle.GetComponentsInChildren<TrailRenderer>();
        ResetGrid();
    }
    void ResetGrid()
    {
        Driver=new FalconDrive {position=Circuit.spawn,heading=Circuit.angle};
        Trial=new TwoLapTrial {previous=Circuit.progress};
        cameraAngle=Driver.heading;cameraRoll=0;Notice=null;controls=default;
        foreach(var trail in exhaust){trail.emitting=false;trail.Clear();}
        Pose();
    }
    public void StartRace()
    {
        ResetGrid(); State=Mode.Countdown;Countdown=3;
    }
    void OnApplicationFocus(bool focus) { if(!focus) controls=default; }
    void Update()
    {
        if(Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            if(State==Mode.Ready || State==Mode.Results) StartRace();
        }
        if(Input.GetKeyDown(KeyCode.Escape)){State=Mode.Ready;ResetGrid();}
        if(State==Mode.Ready) menuTime+=Time.deltaTime;
        if(State!=Mode.Racing && State!=Mode.Countdown) return;
        controls.gas=Input.GetKey(KeyCode.UpArrow);
        controls.brake=Input.GetKey(KeyCode.DownArrow)||Input.GetKey(KeyCode.S);
        controls.turn=((Input.GetKey(KeyCode.RightArrow)||Input.GetKey(KeyCode.D))?1:0)
            -((Input.GetKey(KeyCode.LeftArrow)||Input.GetKey(KeyCode.A))?1:0);
        controls.drift=Input.GetKey(KeyCode.LeftShift)||Input.GetKey(KeyCode.RightShift);
        if(State==Mode.Countdown) return;
        controls.driftPressed|=Input.GetKeyDown(KeyCode.LeftShift)||Input.GetKeyDown(KeyCode.RightShift);
        controls.miniPressed|=Input.GetKeyDown(KeyCode.W)||Input.GetKeyDown(KeyCode.UpArrow);
        controls.boostPressed|=Input.GetKeyDown(KeyCode.Space)||Input.GetKeyDown(KeyCode.LeftControl)||Input.GetKeyDown(KeyCode.RightControl);
        if(Input.GetKeyDown(KeyCode.R))
        {
            int index=Circuit.Nearest(Driver.position,out var p,out _);
            var t=Circuit.Tangent(index);
            Driver.position=p; Driver.heading=Atan2(t.x,t.y);
            Driver=new FalconDrive {position=p,heading=Driver.heading,stock=Driver.stock,charge=Driver.charge};
            cameraAngle=Driver.heading;Pose();
        }
    }
    void FixedUpdate()
    {
        if(State==Mode.Countdown)
        {
            Countdown-=StepTime;
            if(Countdown<=0) {State=Mode.Racing;Flash("出发");}
            return;
        }
        if(State!=Mode.Racing) return;
        Driver.Step(controls,StepTime);
        controls.miniPressed=controls.boostPressed=controls.driftPressed=false;
        int oldLap=Trial.lap;
        Trial.Advance(Circuit.ResolveWall(Driver,StepTime),StepTime);
        if(Driver.notice!=null) Flash(Driver.notice);
        if(oldLap!=Trial.lap) Flash("最后一圈");
        if(Trial.finished) State=Mode.Results;
        Pose();
    }
    void Flash(string text){Notice=text; noticeUntil=Time.time+1;}
    void Pose()
    {
        vehicle.position=new Vector3(Driver.position.x,.05f,Driver.position.y);
        vehicle.rotation=Quaternion.Euler(0,Driver.heading*Rad2Deg,0);
    }
    void LateUpdate()
    {
        if(Time.time>noticeUntil) Notice=null;
        foreach(var trail in exhaust) trail.emitting=State==Mode.Racing && (Driver.nitroTime>0 || Driver.miniTime>0);
        if(State==Mode.Ready)
        {
            // Low front-quarter view with clear space at the left for the event card.
            float angle=.21f*Sin(menuTime*.18f);
            Vector3 offset=Quaternion.Euler(0,angle*Rad2Deg,0)*new Vector3(3.7f,1.9f,4.5f);
            chase.transform.position=vehicle.TransformPoint(offset);
            chase.transform.LookAt(vehicle.TransformPoint(new Vector3(1.4f,.65f,-.8f)));
            chase.fieldOfView=45;return;
        }
        float dt=Time.deltaTime, target=Driver.heading-(Driver.Rebounding?0:Driver.slip*.65f);
        cameraAngle+=FalconDrive.Delta(target,cameraAngle)*(1-Exp(-10*dt));
        var dir=new Vector3(Sin(cameraAngle),0,Cos(cameraAngle));
        var pos=vehicle.position; chase.transform.position=pos-13*dir+Vector3.up*5.65f;
        chase.transform.LookAt(pos+7*dir+Vector3.up*1.15f);
        float roll=Sign(Driver.slip)*Clamp01((Abs(Driver.slip)*Rad2Deg-15)/65)*10;
        cameraRoll=Lerp(cameraRoll,roll,1-Exp(-8*dt));
        chase.transform.Rotate(0,0,cameraRoll);
        float fov=64+(Driver.nitroTime>0?(Driver.miniTime>0?8:4):0);
        chase.fieldOfView=Lerp(chase.fieldOfView,fov,1-Exp(-(fov>chase.fieldOfView?15:8.6f)*dt));
    }
}
