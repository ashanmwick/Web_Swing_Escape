using UnityEngine;

/// <summary>
/// Configures and drives a real Unity <see cref="ParticleSystem"/> for the
/// XP-point pickup effect. Each particle is a billboarded image (a sprite/texture
/// on the particle material): it spawns along the bottom line and rises a fixed,
/// tunable distance (<see cref="riseHeight"/>) to the top, scaling up a little and
/// then gradually shrinking and fading to nothing on the way.
///
/// Put this on its OWN GameObject with a <see cref="ParticleSystem"/> (the
/// component adds one). It does NOT need to be under the Canvas &mdash; a
/// ParticleSystem is a world renderer, so it should sit in the scene where a
/// camera can see it. Every module is baked from code in <see cref="Apply"/>;
/// use the context-menu items below to (re)bake, test and diagnose.
/// </summary>
[RequireComponent(typeof(ParticleSystem))]
[ExecuteAlways]
public class XpPointParticles : MonoBehaviour
{
    [Header("Look")]
    [Tooltip("Sprite drawn for every particle. Its texture is pushed onto the particle material.")]
    [SerializeField] Sprite particleSprite;
    [Tooltip("Material for the particle renderer. Leave empty to auto-create an unlit alpha-blended one.")]
    [SerializeField] Material particleMaterial;
    [Tooltip("Start colour / tint (RGB). Alpha is driven by the fade curve below.")]
    [SerializeField] Color startColor = new Color(1f, 0.86f, 0.3f, 1f);
    [Tooltip("Additive blending (glowy). Off = normal alpha blend.")]
    [SerializeField] bool additive = true;
    [Tooltip("Animate on unscaled time, so the burst keeps moving while the game is paused (Time.timeScale = 0).")]
    [SerializeField] bool ignoreTimeScale = false;

    [Header("Size")]
    [Tooltip("Size at birth, in WORLD units (independent of this object's scale).")]
    [SerializeField] float startSize = 0.5f;
    [Tooltip("Largest size a particle reaches — it grows from Start Size up to this, then shrinks to nothing.")]
    [SerializeField] float maxSize = 1.2f;
    [Tooltip("When in its life a particle hits Max Size (0 = birth, 1 = death).")]
    [SerializeField, Range(0.05f, 0.9f)] float maxSizeTime = 0.25f;
    [Tooltip("Random +/- fraction on size, per particle (0.3 = +/-30%). Applied to both Start and Max size.")]
    [SerializeField, Range(0f, 1f)] float startSizeJitter = 0.25f;

    [Header("Motion")]
    [Tooltip("Seconds each particle lives.")]
    [SerializeField] float lifetime = 0.95f;
    [Tooltip("How far UP a particle travels over its life, from the spawn line. This is a real distance — the particle ends this many units above where it started.")]
    [SerializeField] float riseHeight = 3f;
    [Tooltip("Random +/- fraction on rise height per particle (0.25 = +/-25%).")]
    [SerializeField, Range(0f, 1f)] float riseHeightJitter = 0.2f;
    [Tooltip("Ease out the ascent — fast off the bottom, decelerating to a stop at the top. Off = constant speed.")]
    [SerializeField] bool easeOut = true;
    [Tooltip("Random sideways speed at birth.")]
    [SerializeField] float sidewaysSpeed = 0.5f;
    [Tooltip("Width of the bottom emission line (local X).")]
    [SerializeField] float spawnWidth = 2f;
    [Tooltip("How far below the origin the spawn line sits (local Y). Particles rise from here to here + Rise Height.")]
    [SerializeField] float spawnYOffset = 0.6f;

    [Header("Emission")]
    [Tooltip("Particles per burst.")]
    [SerializeField] int particlesPerBurst = 14;
    [Tooltip("Seconds between automatic bursts while the system is playing.")]
    [SerializeField] float burstInterval = 1.2f;
    [Tooltip("Extra continuous emission on top of the bursts. 0 = bursts only.")]
    [SerializeField] float rateOverTime = 0f;
    [Tooltip("Play automatically when this object is enabled.")]
    [SerializeField] bool playOnAwake = true;
    [Tooltip("Animate the effect in the editor without entering Play mode (drives the sim from Update).")]
    [SerializeField] bool editorPreview = true;

    ParticleSystem _ps;
    ParticleSystemRenderer _renderer;
    float _lastSpeedForHeight;
    double _lastPreviewTime;

    void Reset()               => Cache(rebake: true);
    void Awake()               => Cache(rebake: true);
    void OnValidate()
    {
        // Re-bake live while tuning in the editor; in Play mode wait for an explicit Apply().
        if (!Application.isPlaying) Cache(rebake: true);
        else Cache(rebake: false);
    }

    void OnEnable()
    {
        Cache(rebake: false);
        _lastPreviewTime = Time.realtimeSinceStartupAsDouble;
        if (Application.isPlaying && playOnAwake) Play();
    }

    void Update()
    {
        if (_ps == null) return;

        if (Application.isPlaying)
        {
            // Keep it alive even if something stopped the system.
            if (playOnAwake && !_ps.isPlaying && !_ps.isPaused) _ps.Play(true);
            return;
        }

        // Editor-only: advance the simulation so the burst visibly moves without Play.
        if (!editorPreview) return;
        double now = Time.realtimeSinceStartupAsDouble;
        float dt = Mathf.Clamp((float)(now - _lastPreviewTime), 0f, 0.05f);
        _lastPreviewTime = now;
        if (!_ps.isPlaying) _ps.Play(true);
        _ps.Simulate(dt, true, false, false);
    }

    void Cache(bool rebake)
    {
        if (_ps == null) _ps = GetComponent<ParticleSystem>();
        if (_renderer == null) _renderer = GetComponent<ParticleSystemRenderer>();
        if (rebake && _ps != null) Apply();
    }

    // ---- Public API -------------------------------------------------------

    /// <summary>Start the system. Bursts then repeat every <c>burstInterval</c> automatically.</summary>
    [ContextMenu("Play")]
    public void Play()
    {
        Cache(rebake: false);
        _ps.Clear(true);
        _ps.Play(true);
    }

    /// <summary>Stop emitting and clear live particles.</summary>
    [ContextMenu("Stop")]
    public void Stop()
    {
        Cache(rebake: false);
        _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    /// <summary>Fire one extra burst right now (independent of the auto-burst loop).</summary>
    [ContextMenu("Emit One Burst")]
    public void Emit() => Emit(particlesPerBurst);

    /// <summary>Fire <paramref name="count"/> particles immediately.</summary>
    public void Emit(int count)
    {
        Cache(rebake: false);
        if (!_ps.isPlaying) _ps.Play(true);
        _ps.Emit(count);
    }

    // ---- Baking ---------------------------------------------------------

    /// <summary>Push every serialized field onto the ParticleSystem modules.</summary>
    [ContextMenu("Apply Settings")]
    public void Apply()
    {
        Cache(rebake: false);
        if (_ps == null) return;

        float cycle = Mathf.Max(0.05f, burstInterval);

        // Motion is driven by startSpeed (a straight-up launch) + gravityModifier
        // (a gentle deceleration). Both are core-simulation features, applied every
        // step regardless of module state — unlike Velocity-over-Lifetime, which is
        // what previously failed to move the particles. With initial speed v0 and
        // deceleration g where g = v0 / lifetime, a particle rises exactly
        // v0*L - 0.5*g*L^2 over its life; pick v0 so that equals riseHeight.
        float life = Mathf.Max(0.05f, lifetime);
        float v0, g;
        if (easeOut)
        {
            v0 = 2f * riseHeight / life;          // decelerates to a stop at the top
            g  = 2f * riseHeight / (life * life);
        }
        else
        {
            v0 = riseHeight / life;               // constant speed
            g  = 0f;
        }
        _lastSpeedForHeight = v0;

        var main = _ps.main;
        main.duration = cycle;                 // burst at t=0 repeats once per loop cycle
        main.loop = true;
        main.prewarm = false;
        main.playOnAwake = playOnAwake;
        main.startLifetime = life;
        main.startSpeed = new ParticleSystem.MinMaxCurve(
            v0 * (1f - riseHeightJitter), v0 * (1f + riseHeightJitter));
        main.startSize = new ParticleSystem.MinMaxCurve(
            startSize * (1f - startSizeJitter), startSize * (1f + startSizeJitter));
        main.startColor = startColor;
        // gravityModifier is a multiple of Physics.gravity (y ≈ -9.81), so a positive
        // value pulls DOWN — exactly the deceleration we want for the upward launch.
        main.gravityModifier = g / Mathf.Max(0.01f, Mathf.Abs(Physics.gravity.y));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Shape;
        main.useUnscaledTime = ignoreTimeScale;
        main.maxParticles = 1000;

        var emission = _ps.emission;
        emission.enabled = true;
        emission.rateOverTime = Mathf.Max(0f, rateOverTime);
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Mathf.Max(1, particlesPerBurst)) });

        // Thin box along the bottom, rotated so its emission normal (+Z) points up,
        // so startSpeed launches particles straight up.
        var shape = _ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(Mathf.Max(0.01f, spawnWidth), 0.01f, 0.01f);
        shape.position = new Vector3(0f, -spawnYOffset, 0f);
        shape.rotation = new Vector3(-90f, 0f, 0f);

        // Velocity module now only adds a little sideways drift; the rise is startSpeed.
        var vel = _ps.velocityOverLifetime;
        vel.enabled = sidewaysSpeed > 0f;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(-sidewaysSpeed, sidewaysSpeed);
        vel.y = new ParticleSystem.MinMaxCurve(0f);
        vel.z = new ParticleSystem.MinMaxCurve(0f);

        var limit = _ps.limitVelocityOverLifetime;
        limit.enabled = false;

        var sol = _ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, SizeShape());

        var col = _ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[]
            {
                new GradientAlphaKey(0.15f, 0f),
                new GradientAlphaKey(1f, 0.15f),
                new GradientAlphaKey(1f, 0.55f),
                new GradientAlphaKey(0f, 1f),
            });
        col.color = grad;

        ApplyRenderer();

        // Warn about the classic "nothing shows" setup: a ParticleSystem is a world
        // renderer and will not draw on a Screen-Space-Overlay canvas.
        if (GetComponentInParent<Canvas>() != null)
            Debug.LogWarning($"[{name}] XpPointParticles is under a Canvas. A ParticleSystem is a world " +
                             "renderer and will NOT draw on a Screen-Space-Overlay canvas — move it into the scene.", this);
        if (transform.lossyScale.x < 0.01f)
            Debug.LogWarning($"[{name}] XpPointParticles world scale is {transform.lossyScale} — the emission line " +
                             "collapses to a point (particle size/rise are unaffected in Shape scaling mode). " +
                             "Set this object's scale to 1.", this);
        if (GetComponentInParent<Billboard>() != null)
            Debug.LogWarning($"[{name}] XpPointParticles is under a Billboard object, which spins its transform every " +
                             "frame. With World simulation space that randomises the launch direction — put this " +
                             "effect on a standalone GameObject, not inside the Billboard hierarchy.", this);
    }

    void ApplyRenderer()
    {
        if (_renderer == null) return;

        _renderer.enabled = true;
        _renderer.renderMode = ParticleSystemRenderMode.Billboard;
        _renderer.alignment = ParticleSystemRenderSpace.View;
        _renderer.sortMode = ParticleSystemSortMode.None;

        Material mat = particleMaterial;
        if (mat == null)
        {
            // Prefer the URP particle shader (this project is URP); fall back to the
            // built-ins, then the legacy additive shader.
            var urp = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            var shader = urp
                         ?? Shader.Find("Sprites/Default")
                         ?? Shader.Find("Legacy Shaders/Particles/Additive");
            if (shader != null)
            {
                mat = new Material(shader) { name = "XpPointParticles (auto)", hideFlags = HideFlags.DontSave };
                if (shader == urp)
                    ConfigureUrpTransparent(mat, additive);
                else if (additive && shader.name == "Sprites/Default")
                {
                    // Sprites/Default can't do additive; grab the legacy shader instead.
                    var add = Shader.Find("Legacy Shaders/Particles/Additive");
                    if (add != null) mat.shader = add;
                }
            }
            else
            {
                Debug.LogError($"[{name}] No usable particle shader found — assign a Material manually.", this);
            }
        }

        if (mat != null)
        {
            mat.mainTexture = particleSprite != null ? particleSprite.texture : Texture2D.whiteTexture;
            if (mat.HasProperty("_BaseMap") && particleSprite != null)
                mat.SetTexture("_BaseMap", particleSprite.texture);
            if (mat.HasProperty("_Color")) mat.color = Color.white;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            _renderer.sharedMaterial = mat;
        }
    }

    // Force a runtime-created "Universal Render Pipeline/Particles/Unlit" material into
    // a transparent (additive or alpha) blend state. Setting the blend factors + queue
    // + keyword directly is what actually changes the draw; the _Surface/_Blend props
    // are set too so the material inspector reads correctly.
    static void ConfigureUrpTransparent(Material mat, bool additive)
    {
        mat.SetFloat("_Surface", 1f);                 // 0 opaque, 1 transparent
        mat.SetFloat("_Blend", additive ? 1f : 0f);   // 0 alpha, 1 additive (premultiply handled below)
        mat.SetFloat("_ZWrite", 0f);
        if (mat.HasProperty("_SrcBlend"))
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (mat.HasProperty("_DstBlend"))
            mat.SetFloat("_DstBlend", additive
                ? (float)UnityEngine.Rendering.BlendMode.One
                : (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }

    // Size-over-life as a multiplier on startSize: 1 at birth -> (maxSize/startSize)
    // at maxSizeTime -> 0 at death. So a particle grows from startSize up to maxSize,
    // then shrinks to nothing.
    AnimationCurve SizeShape()
    {
        float peak = Mathf.Max(0.01f, maxSize) / Mathf.Max(0.01f, startSize);
        return new AnimationCurve(
            new Keyframe(0f, 1f),
            new Keyframe(Mathf.Clamp(maxSizeTime, 0.05f, 0.9f), peak),
            new Keyframe(1f, 0f));
    }

    // ---- Diagnostics ----------------------------------------------------

    [ContextMenu("Diagnose")]
    void Diagnose()
    {
        Cache(rebake: false);
        var cam = Camera.main;
        string mat = _renderer != null && _renderer.sharedMaterial != null
            ? $"{_renderer.sharedMaterial.name} / shader '{_renderer.sharedMaterial.shader.name}'"
            : "<none>";
        var vom = _ps.velocityOverLifetime;
        var m = _ps.main;
        Debug.Log(
            $"[{name}] Diagnose\n" +
            $"  playing={_ps.isPlaying} particleCount={_ps.particleCount} time={_ps.time:0.00} timeScale={Time.timeScale}\n" +
            $"  worldPos={transform.position} lossyScale={transform.lossyScale}\n" +
            $"  riseHeight={riseHeight} easeOut={easeOut} -> startSpeed≈{_lastSpeedForHeight:0.00} u/s gravityModifier={m.gravityModifierMultiplier:0.00}  (sideways velModule={vom.enabled})\n" +
            $"  simSpace={m.simulationSpace} scalingMode={m.scalingMode} useUnscaledTime={m.useUnscaledTime}\n" +
            $"  rendererEnabled={(_renderer != null && _renderer.enabled)} material={mat}\n" +
            $"  underCanvas={GetComponentInParent<Canvas>() != null} activeInHierarchy={gameObject.activeInHierarchy}\n" +
            $"  mainCamera={(cam != null ? cam.name : "<none>")}", this);
        if (cam != null)
        {
            Vector3 vp = cam.WorldToViewportPoint(transform.position);
            bool onScreen = vp.z > 0f && vp.x is > 0f and < 1f && vp.y is > 0f and < 1f;
            Debug.Log($"[{name}] viewportPoint={vp} onScreen={onScreen}", this);
        }
    }

    /// <summary>Sanity check: teleport 4 units in front of the main camera and blast a burst.</summary>
    [ContextMenu("TEST: Play In Front Of Camera")]
    void TestInFrontOfCamera()
    {
        var cam = Camera.main;
        if (cam == null) { Debug.LogWarning($"[{name}] No Camera.main tagged."); return; }
        transform.SetParent(null, true);
        transform.position = cam.transform.position + cam.transform.forward * 4f;
        Apply();
        Play();
        Debug.Log($"[{name}] Moved to {transform.position} and playing. If you still see nothing, it's the material.", this);
    }
}
