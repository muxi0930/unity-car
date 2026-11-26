using UnityEngine;
using static UnityEngine.Mathf;

public struct DriveInput
{
    public int turn;
    public bool gas, brake, drift, driftPressed, miniPressed, boostPressed;
}

// Blue Falcon, with the reference game's public/tuning.json overrides applied.
// X and Y here are the horizontal Unity X and Z axes. Angles are radians.
public sealed class FalconDrive
{
    public Vector2 position, velocity;
    public float heading, speed, slip, charge, miniTime, nitroTime, miniWindow;
    public int stock, miniCharges;
    public bool drifting, reversing;
    public string notice, combo;
    enum Phase { Grip, Entry, Drift, Point, Sustain, Recover }
    Phase phase;
    bool driftLock, held, controlHeld, collected, prefixed;
    float clock, driftTime, driftAngle, counterTime, holdTime, pointTime, pointDuration;
    float cutWindow, cutTime, cooldown, miniCooldown, nitroStart, firstMini, firstDelay, factor, yaw;
    int direction, taps, comboMinis, used;
    Vector2? wall, rebound;
    public bool Rebounding => rebound.HasValue;
    public static float Delta(float a, float b) => Atan2(Sin(a-b),Cos(a-b));
    static float Smooth(float value) { float t=Clamp01(value); return t*t*(3-2*t); }
    static float Tick(float t, float dt) => Max(0,t-dt);
    static Vector2 Forward(float a) => new Vector2(Sin(a),Cos(a));
    const float NormalResistance=.00125f, Cruise=225/3.6f, Mini=270/3.6f, Nitro=305/3.6f;

    void EndDrift(bool clean)
    {
        if (clean && driftTime>=.25f && driftAngle>=.22f)
        {
            miniCharges=counterTime>=.075f?2:1;
            miniWindow=1.35f; used=0;
            notice=miniCharges==2?"双喷就绪  ·  W × 2":"短喷就绪  ·  W";
        }
        drifting=false; phase=Phase.Grip; controlHeld=false; holdTime=0;
        pointTime=pointDuration=cutWindow=0; taps=0;
    }

    void Boosts(DriveInput input)
    {
        if (!input.gas)
        {
            nitroTime=miniTime=factor=0; combo=""; comboMinis=0; prefixed=false; return;
        }
        if (input.boostPressed && stock>0 && nitroTime==0)
        {
            stock--; nitroTime=3.1f; nitroStart=clock; comboMinis=0; factor=0;
            prefixed=miniTime>0; miniTime*=.8f/.66f;
            combo=prefixed?"WC":""; notice=prefixed?"WC  ·  氮气叠喷":"氮气加速";
        }
        if (!input.miniPressed || miniCharges==0 || miniCooldown>0) return;
        miniCharges--; used++; miniCooldown=.12f;
        miniWindow=miniCharges>0?1:0; miniTime=nitroTime>0?.8f:.66f;
        notice=used==2?"双喷":"短喷";
        if (nitroTime<=0) return;
        if (comboMinis==0) { firstDelay=clock-nitroStart; firstMini=clock; }
        comboMinis=Min(2,comboMinis+1);
        float gap=clock-firstMini;
        float second=Clamp01(1-Max(.55f-gap,gap-.85f,0)/.7f);
        factor=(comboMinis==2?(prefixed?.9f:.8f)*second:(prefixed?.4f:.3f))*Clamp01(1-Max(0,firstDelay-.25f)/.7f);
        combo=(prefixed?"W":"")+"C"+(comboMinis==2?"WW":"W"); notice=combo+"  ·  氮气叠喷";
    }

    void Steering(DriveInput input, float dt, float travel)
    {
        int turn=input.turn;
        bool press=input.driftPressed || (input.drift && !held);
        held=input.drift;
        if (!held) driftLock=false;
        if (!reversing && !drifting && !driftLock && cutTime==0 && held && turn!=0 && speed>12)
        {
            drifting=driftLock=controlHeld=true; collected=false; direction=turn;
            driftTime=driftAngle=counterTime=0; phase=Phase.Entry; miniCharges=0; miniWindow=0;
        }
        if (drifting && speed<8) EndDrift(false);
        if (drifting) driftTime+=dt;
        bool counter=drifting && turn==-direction;
        if (counter) counterTime+=dt; else {taps=0;cutWindow=0;}
        if (counter && press)
        {
            taps++; cutWindow=.32f;
            if (taps==2)
            {
                if (driftTime>=.5f) {EndDrift(true);cutTime=.1f;driftLock=true;yaw=0;}
                taps=0;cutWindow=0;
            }
        }
        if (drifting && held) driftLock=true;
        holdTime=drifting && held?Min(1.2f,holdTime+dt):0;
        bool active=drifting && held && turn==direction;
        float angle=Abs(Delta(heading,travel));
        if (drifting)
        {
            if (active) phase=angle<.48f?Phase.Entry:Phase.Drift;
            else if (counter && phase!=Phase.Point) phase=Phase.Recover;
            else if (controlHeld)
            {
                phase=angle<=10*Deg2Rad?Phase.Point:angle>PI/4?Phase.Sustain:Phase.Recover;
                pointTime=0;pointDuration=Max(.25f,(1/(speed*.9f)-1/speed)/.006f);
            }
            controlHeld=active;
        }
        float hold=Smooth(holdTime/1.2f);
        float rate=active?2.35f:counter?2.4f:.9f;
        rate*=active||counter?1+.18f*hold:drifting?1:45/Max(45,speed);
        float target=cutTime>0?0:turn*rate;
        bool canTurn=speed>.1f || ((wall.HasValue || rebound.HasValue) && input.gas);
        yaw=turn==0 || !canTurn?0:drifting?Lerp(yaw,target,1-Exp(-(counter?26:20)*(1+.65f*hold)*dt)):target;
        if (reversing) yaw=-yaw;
        heading+=yaw*dt;
        if (drifting) driftAngle+=Abs(yaw)*dt;
        if (phase==Phase.Sustain && Abs(Delta(heading,travel))<=PI/4) phase=Phase.Recover;
    }

    public void Step(DriveInput input, float dt)
    {
        notice=null; clock+=dt;
        bool hadNitro=nitroTime>0;
        miniWindow=Tick(miniWindow,dt); miniTime=Tick(miniTime,dt); miniCooldown=Tick(miniCooldown,dt);
        nitroTime=Tick(nitroTime,dt); cooldown=Tick(cooldown,dt); cutWindow=Tick(cutWindow,dt);cutTime=Tick(cutTime,dt);
        if (hadNitro && nitroTime==0) miniTime*=.66f/.8f;
        if (miniWindow==0) miniCharges=0;
        if (cutWindow==0) taps=0;
        float travel=speed>.1f?Atan2(velocity.x,velocity.y):heading;
        Steering(input,dt,travel); Boosts(input);
        if (speed==0) reversing=input.brake && !input.gas;
        float beta=drifting?Delta(heading,travel):0;
        float degrees=Min(90,Abs(beta)*Rad2Deg), sine=Sin(degrees*Deg2Rad);
        float weight=1-Log(1+2*(1-sine*sine))/Log(3);
        float lift45=.55f-(1-Log(2)/Log(3)), lift60=.7f-(1-Log(1.5f)/Log(3));
        if (degrees>30 && degrees<90)
            weight+=degrees<=45?lift45*Smooth((degrees-30)/15):degrees<=60?Lerp(lift45,lift60,Smooth((degrees-45)/15)):lift60*(1-Smooth((degrees-60)/30));
        float peak=phase==Phase.Sustain?.008f*1.15f:phase==Phase.Point?.009f:
            phase==Phase.Entry || phase==Phase.Drift?.139819f*.75f:NormalResistance;
        float drag=(NormalResistance+(peak-NormalResistance)*weight)*speed*speed;
        float drive=input.gas?(phase==Phase.Sustain?.65f*Min(speed,62):37*(1-.8f*Smooth(speed/Cruise))):0;
        if (drifting) drive=Min(drive,drag*.9f+Max(0,100/3.6f-speed)/dt);
        bool nitro=nitroTime>0, mini=miniTime>0;
        float thrust=drive+((nitro?30:0)+(mini?30:0))*(nitro&&mini?.7f:1);
        float coast=!input.gas && !input.brake && !reversing?10:0;
        float accel=thrust-drag-coast-(input.brake?52:0);
        float grip=speed>.1f?Clamp01(thrust*dt/speed*(Abs(beta)>1e-8f?Sin(beta)/beta:1)):0;
        float roadRate=phase==Phase.Sustain?.8f:phase==Phase.Recover?2.8f:phase==Phase.Point?1.3f:.12f+1.35f*Smooth((Abs(beta)-.18f)/.75f);
        roadRate*=phase==Phase.Recover?.75f:.258806f*.7f;
        if (input.brake) roadRate+=5.5f;
        float move=drifting?travel+(1-(1-grip)*Exp(-roadRate*dt))*beta:heading;
        if (reversing) {move=heading+PI;accel=input.gas?-52:input.brake?18-drag:-8-drag;}
        slip=reversing?0:Delta(heading,move);
        if (phase==Phase.Sustain && Abs(slip)<=PI/4) phase=Phase.Recover;
        if (phase==Phase.Point) pointTime+=dt;
        if (drifting && !input.drift && ((phase==Phase.Point && pointTime>=pointDuration) || (phase==Phase.Recover && Abs(slip)<13*Deg2Rad)))
        {EndDrift(true); move=heading; slip=0;}
        float limit=reversing?12:Cruise+(nitro?Nitro-Cruise:0)+(mini?(Mini-Cruise)*(nitro?factor:1):0);
        if (speed>limit) accel=Min(accel,-(speed-limit)*1.96f-2.1f);
        float before=speed;
        speed=Max(0,speed+dt*accel);
        if (before<=limit && accel>0) speed=Min(limit,speed);
        var dir=Forward(move);
        if (drifting || (rebound.HasValue && Vector2.Dot(dir,rebound.Value)<=0)) rebound=null;
        if (!drifting && (rebound.HasValue || (wall.HasValue && Vector2.Dot(dir,wall.Value)>0)))
        {
            float push=reversing?(input.brake&&!input.gas?18:0):thrust;
            float brake=reversing?(input.gas?52:input.brake?0:8):(input.brake?52:0);
            velocity=velocity*(before>0?Max(0,1-(drag+coast+brake)*dt/before):0)+dir*push*dt;
            if (rebound.HasValue && !wall.HasValue)
            {
                var side=new Vector2(Cos(heading),-Sin(heading));
                var normal=rebound.Value;
                float into=Vector2.Dot(velocity,normal);
                velocity-=side*Vector2.Dot(side,velocity)*(1-Exp(-8*dt));
                if (into<=0) velocity-=normal*Max(0,Vector2.Dot(velocity,normal));
            }
            velocity=Vector2.ClampMagnitude(velocity,before>limit?speed:limit);
            speed=velocity.magnitude;
            slip=speed>.1f?Delta(heading,Atan2(velocity.x,velocity.y)):0;
            if (speed<.1f || (rebound.HasValue && Abs(Delta(move,Atan2(velocity.x,velocity.y)))<.02f && Vector2.Dot(velocity,rebound.Value)>=0)) rebound=null;
        }
        else velocity=dir*speed;
        wall=null; position+=velocity*dt;
        if (drifting && cooldown==0 && !collected)
        {
            charge=Min(1,charge+dt*Max(.12f,Max(0,drag-NormalResistance*before*before)*before*.00052f*.7f));
            if (charge>=1) {collected=true;if(stock<2){stock++;charge=0;notice="氮气 +1";}}
        }
    }

    public void Collide(Vector2 normal, float penetration, float dt)
    {
        wall=normal; position-=normal*penetration;
        float impact=Vector2.Dot(velocity,normal);
        if (impact<0) return;
        var tangent=velocity-normal*impact;
        float speedAlong=tangent.magnitude;
        float friction=speedAlong>0?Min(1,.008f*speedAlong*dt+.25f*impact/speedAlong):0;
        velocity=tangent*(1-friction)-normal*impact*.1f;
        if (impact>0) rebound=normal;
        if (cooldown==0 && impact>4)
        {
            velocity*=impact>20?.65f:.94f; notice="擦碰";
            if (impact>12) {miniWindow=0;miniCharges=0;charge*=.5f;cutTime=0;EndDrift(false);}
            cooldown=.22f;
        }
        speed=velocity.magnitude;
    }
}
