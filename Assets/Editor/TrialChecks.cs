using System;
using UnityEditor;
using UnityEngine;

public static class TrialChecks
{
    const float Delta=1f/120;
    static void Check(bool pass,string reason)
    {
        if(!pass) throw new Exception(reason);
    }
    static void Near(float actual,float expected)
    {
        Check(Mathf.Abs(actual-expected)<.001f,$"Replay mismatch: {actual} versus {expected}");
    }
    static void Tick(FalconDrive car,int frames,int turn=0,bool drift=false,bool mini=false,bool boost=false)
    {
        for(int i=0;i<frames;i++)
            car.Step(new DriveInput {gas=true,turn=turn,drift=drift,miniPressed=mini,boostPressed=boost},Delta);
    }
    // Expected samples taken from balanced + public/tuning.json, 120 Hz.
    static void ReferenceReplay()
    {
        var car=new FalconDrive();
        Tick(car,480); Near(car.speed,57.730761f);
        Tick(car,72,1,true); Near(car.speed,29.086750f); Near(car.slip,1.148682f); Near(car.charge,.543587f);
        Tick(car,12,-1);Tick(car,1,-1,true);Tick(car,1,-1);Tick(car,1,-1,true);
        Near(car.speed,29.263470f); Check(car.miniCharges==2 && !car.drifting,"Cut drift");
        Tick(car,1);Tick(car,1,mini:true); Near(car.miniTime,.66f); Near(car.speed,29.887895f);
        Tick(car,76);Near(car.miniTime,.026667f);Near(car.speed,56.329086f);
        car.stock=1; Tick(car,1,boost:true); Check(car.combo=="WC","WC");Near(car.nitroTime,3.1f);
        Tick(car,1,mini:true);Check(car.combo=="WCW","WCW");Near(car.miniTime,.8f);
        var cw=new FalconDrive {stock=1,miniCharges=2,miniWindow=1.35f};
        Tick(cw,1,boost:true);Tick(cw,1,mini:true);Check(cw.combo=="CW","CW");
        Tick(cw,75);Tick(cw,1,mini:true);Check(cw.combo=="CWW","CWW");
        var wall=new FalconDrive {velocity=new Vector2(10,30),speed=Mathf.Sqrt(1000)};
        wall.Collide(Vector2.right,.2f,Delta);Check(wall.velocity.x<0 && wall.velocity.y>0 && wall.speed<30,"Wall impulse");
    }

    [MenuItem("Photon Fox/Verify time trial")]
    public static void Run()
    {
        ReferenceReplay();
        var circuit=JsonUtility.FromJson<CityCircuit>(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Art/Route.json").text);
        var car=new FalconDrive {position=circuit.spawn,heading=circuit.angle};
        var race=new TwoLapTrial {previous=circuit.progress};
        for(int i=0;i<120*420 && !race.finished;i++)
        {
            int at=circuit.Nearest(car.position,out _,out _);
            var target=circuit.points[(at+7)%circuit.points.Length]-car.position;
            float error=FalconDrive.Delta(Mathf.Atan2(target.x,target.y),car.heading);
            float bend=Vector2.Angle(circuit.Tangent(at),circuit.Tangent((at+15)%circuit.points.Length));
            float speedTarget=bend>22?12:bend>10?17:28;
            car.Step(new DriveInput {turn=Mathf.Abs(error)<.012f?0:(int)Mathf.Sign(error),gas=car.speed<speedTarget,brake=car.speed>speedTarget+1},Delta);
            race.Advance(circuit.ResolveWall(car,Delta),Delta);
        }
        Check(race.finished && race.splits[0]>0 && race.splits[1]>0,$"Driver stuck at {race.previous} in lap {race.lap}");
        var wrong=new TwoLapTrial {previous=.98f}; wrong.Advance(.01f,Delta);
        Check(wrong.lap==1 && wrong.checkpoint==0,"False lap at grid");
        Debug.Log($"PHOTON PASS: reference drift/boost replay, wall, checkpoint and driven laps {race.splits[0]:F2}s / {race.splits[1]:F2}s");
    }
}
