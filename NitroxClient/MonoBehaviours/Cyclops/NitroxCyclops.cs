using NitroxClient.GameLogic;
using NitroxClient.GameLogic.PlayerLogic;
using NitroxClient.GameLogic.PlayerLogic.PlayerModel.Abstract;
using UnityEngine;

namespace NitroxClient.MonoBehaviours.Cyclops;

/// <summary>
/// Script responsible for managing all player movement-related interactions.
/// </summary>
public class NitroxCyclops : MonoBehaviour
{
    public VirtualCyclops Virtual { get; private set; }
    private CyclopsMotor cyclopsMotor;
    private SubRoot subRoot;
    private SubControl subControl;
    private Rigidbody rigidbody;
    private WorldForces worldForces;
    private Stabilizer stabilizer;
    private CharacterController controller;
    private int ballasts;

    public SubControl.Mode Mode;

    public void Start()
    {
        cyclopsMotor = Player.mainObject.GetComponent<CyclopsMotor>();
        subRoot = GetComponent<SubRoot>();
        subControl = GetComponent<SubControl>();
        rigidbody = GetComponent<Rigidbody>();
        worldForces = GetComponent<WorldForces>();
        stabilizer = GetComponent<Stabilizer>();
        controller = cyclopsMotor.controller;
        ballasts = GetComponentsInChildren<BallastWeight>(true).Length;

        UWE.Utils.SetIsKinematicAndUpdateInterpolation(rigidbody, false, true);

        GetComponent<SubFire>().enabled = false;
        SetReceiving();

        Virtual = VirtualCyclops.CreateVirtualInstance(gameObject);
    }

    /// <remarks>
    /// Triggered by <see cref="LiveMixin"/> sending a "OnKill" message when cyclops is destroyed.
    /// This might need to be adapted once the "restore" command is synced (for now a destroyed cyclops can't be restored)
    /// </remarks>
    public void OnKill()
    {
        VirtualCyclops.Terminate(gameObject);
    }

    /// <summary>
    /// Triggers required on-remove callbacks on children player objects, including the local player.
    /// </summary>
    public void RemoveAllPlayers()
    {
        foreach (RemotePlayerIdentifier remotePlayerIdentifier in GetComponentsInChildren<RemotePlayerIdentifier>(true))
        {
            remotePlayerIdentifier.RemotePlayer.ResetStates();
            OnPlayerExit(remotePlayerIdentifier.RemotePlayer);
        }
        OnLocalPlayerExit();
    }

    /// <summary>
    /// Parents local player to the cyclops and registers it in the virtual cyclops.
    /// </summary>
    public void OnLocalPlayerEnter()
    {
        Player.mainObject.transform.parent = subRoot.transform;
        CyclopsPawn pawn = Virtual.AddPawnForPlayer(this.Resolve<ILocalNitroxPlayer>());
        cyclopsMotor.SetCyclops(subRoot, pawn);
        cyclopsMotor.ToggleCyclopsMotor(true);
    }

    /// <summary>
    /// Unregisters the local player from the cyclops (and from the virtual one). Ensures the player is not weirdly rotated when it leaves the cyclops.
    /// </summary>
    public void OnLocalPlayerExit()
    {
        Virtual.RemovePawnForPlayer(this.Resolve<ILocalNitroxPlayer>());
        Player.main.transform.parent = null;
        Player.main.transform.rotation = Quaternion.identity;
        cyclopsMotor.ToggleCyclopsMotor(false);
    }

    /// <summary>
    /// Registers a remote player in the virtual cyclops.
    /// </summary>
    public void OnPlayerEnter(RemotePlayer remotePlayer)
    {
        remotePlayer.Pawn = Virtual.AddPawnForPlayer(remotePlayer);
    }

    /// <summary>
    /// Unregisters a remote player from the virtual cyclops.
    /// </summary>
    public void OnPlayerExit(RemotePlayer remotePlayer)
    {
        Virtual.RemovePawnForPlayer(remotePlayer);
        remotePlayer.Pawn = null;
    }

    public void MaintainPawns()
    {
        foreach (CyclopsPawn pawn in Virtual.Pawns.Values)
        {
            if (pawn.MaintainPredicate())
            {
                pawn.MaintainPosition();
            }
        }
    }

    public void SetBroadcasting()
    {
        worldForces.OnDisable();
        worldForces.enabled = true;
        stabilizer.stabilizerEnabled = true;
    }

    public void SetReceiving()
    {
        worldForces.enabled = false;
        stabilizer.stabilizerEnabled = false;
    }

    // TODO: all of the below stuff is purely for testing and will probably get removed before merge
    // EXCEPT for the MaintainPawns line in Update()
    private Vector3 forward => subRoot.subAxis.forward;
    private Vector3 up => subRoot.subAxis.up;
    private Vector3 right => subRoot.subAxis.right;

    public bool Autopilot;
    public bool Sinus;
    public bool Rolling;
    public bool Torqing;
    public bool RenderersToggled;

    public float Forward;
    public float Up;
    public float VerticalPeriod = 1f;
    public float Torque;
    public float Roll;

    public void ResetAll()
    {
        Torqing = false;
        Rolling = false;
        rigidbody.velocity = Vector3.zero;
        rigidbody.angularVelocity = Vector3.zero;
        transform.position = new(70f, -16f, 0f);
        transform.rotation = Quaternion.Euler(new(360f, 270f, 0f));

        Player.main.SetCurrentSub(subRoot, true);
        Player.main.SetPosition(transform.position + up);
        cyclopsMotor.ToggleCyclopsMotor(true);
        cyclopsMotor.Pawn.SetReference();

        Log.InGame("Reset player");
    }

    public void Update()
    {
        if (!DevConsole.instance.state)
        {
            if (Input.GetKeyUp(KeyCode.R))
            {
                ResetAll();
            }
            if (Input.GetKeyUp(KeyCode.N))
            {
                SetReceiving();
                Rolling = !Rolling;
                Autopilot = true;
                Roll = Rolling.ToFloat();
                Log.InGame($"Rolling: {Rolling}");
            }
            if (Input.GetKeyUp(KeyCode.B))
            {
                SetReceiving();
                Torqing = !Torqing;
                Autopilot = true;
                Torque = Torqing.ToFloat();
                Log.InGame($"Torqing: {Torqing}");
            }
        }

        MaintainPawns();
    }

    public void FixedUpdate()
    {
        MoveAutopilot();
    }

    public void MoveAutopilot()
    {
        if (!Autopilot)
        {
            return;
        }

        // https://docs.unity3d.com/ScriptReference/Rigidbody.AddTorque.html
        Vector3 cyclopsTorqueFactor = up * subControl.BaseTurningTorque * subControl.turnScale;
        rigidbody.angularVelocity += cyclopsTorqueFactor * Torque * Time.fixedDeltaTime;

        Vector3 cyclopsRollFactor = right * subControl.BaseTurningTorque * subControl.turnScale;
        rigidbody.angularVelocity += cyclopsRollFactor * Roll * Time.fixedDeltaTime;

        // https://docs.unity3d.com/ScriptReference/Rigidbody.AddForce.html
        Vector3 cyclopsVerticalFactor = up * (subControl.BaseVerticalAccel + ballasts * subControl.AccelPerBallast) * subControl.accelScale;
        if (Sinus)
        {
            cyclopsVerticalFactor *= Mathf.Sin(2 * Mathf.PI * Time.fixedTime / VerticalPeriod);
        }
        rigidbody.velocity += cyclopsVerticalFactor * Up * Time.fixedDeltaTime;

        Vector3 cyclopsForwardFactor = forward * subControl.BaseForwardAccel * subControl.accelScale;
        rigidbody.velocity += cyclopsForwardFactor * Forward * Time.fixedDeltaTime;

        subControl.appliedThrottle = true;
    }
}
