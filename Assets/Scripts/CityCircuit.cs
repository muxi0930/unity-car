using System;
using UnityEngine;

[Serializable]
public class CityCircuit
{
    public Vector2[] points;
    public Vector2 spawn;
    public float angle, progress;

    public Vector2 Tangent(int i) => (points[(i+1)%points.Length]-points[i]).normalized;
    public int Nearest(Vector2 position, out Vector2 center, out float progressAt)
    {
        int best=0;
        float min=float.PositiveInfinity;
        center=points[0]; progressAt=0;
        for(int i=0;i<points.Length;i++)
        {
            var a=points[i]; var segment=points[(i+1)%points.Length]-a;
            float t=Mathf.Clamp01(Vector2.Dot(position-a,segment)/segment.sqrMagnitude);
            var p=a+t*segment;
            float distance=(position-p).sqrMagnitude;
            if(distance>=min) continue;
            min=distance; best=i; center=p; progressAt=(i+t)/points.Length;
        }
        return best;
    }
    public float ResolveWall(FalconDrive car,float dt)
    {
        int i=Nearest(car.position,out var center,out float routeProgress);
        var t=Tangent(i);
        var offset=car.position-center;
        float angleToWall=FalconDrive.Delta(car.heading,Mathf.Atan2(t.x,t.y));
        float side=Mathf.Sign(Vector2.Dot(offset,new Vector2(t.y,-t.x)));
        float sin=Mathf.Sin(angleToWall), c=Mathf.Abs(Mathf.Cos(angleToWall));
        float box=.65f*.95f*(3.74f*c+5.85f*Mathf.Abs(sin))/2;
        if(sin*side>0) box-=.3f*(c+Mathf.Abs(sin)-1);
        float depth=offset.magnitude-(10.8f-box);
        if(depth>=-.02f) car.Collide(offset.normalized,Mathf.Max(0,depth),dt);
        return routeProgress;
    }
}

public sealed class TwoLapTrial
{
    public int lap=1, checkpoint;
    public float elapsed, previous;
    public readonly float[] splits=new float[2];
    public bool finished;
    public void Advance(float progress,float dt)
    {
        if(finished) return;
        elapsed+=dt;
        float change=Mathf.Repeat(progress-previous+1.5f,1)-.5f;
        if(change>0 && change<.05f)
        {
            float next=(checkpoint+1)*.25f;
            if(checkpoint<3 && previous<next && progress>=next) checkpoint++;
            if(checkpoint==3 && previous>.95f && progress<.05f)
            {
                splits[lap-1]=elapsed-(lap==2?splits[0]:0);
                checkpoint=0;
                if(lap==2) finished=true; else lap++;
            }
        }
        previous=progress;
    }
}
