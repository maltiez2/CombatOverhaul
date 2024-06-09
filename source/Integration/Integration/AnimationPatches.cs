using CombatOverhaul.Animations;
using CombatOverhaul.Colliders;
using HarmonyLib;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace CombatOverhaul.Integration;

internal static class AnimationPatch
{

    public static event Action<Entity, float>? OnBeforeFrame;
    public static event Action<Entity, ElementPose>? OnFrame;


    public static void Patch(string harmonyId)
    {
        new Harmony(harmonyId).Patch(
                typeof(EntityShapeRenderer).GetMethod("RenderHeldItem", AccessTools.all),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(AnimationPatch), nameof(RenderHeldItem)))
            );

        new Harmony(harmonyId).Patch(
                typeof(EntityShapeRenderer).GetMethod("DoRender3DOpaque", AccessTools.all),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(AnimationPatch), nameof(DoRender3DOpaque)))
            );

        new Harmony(harmonyId).Patch(
                typeof(EntityPlayer).GetMethod("OnSelfBeforeRender", AccessTools.all),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(AnimationPatch), nameof(BeforeRender)))
            );

        new Harmony(harmonyId).Patch(
                typeof(Vintagestory.API.Common.AnimationManager).GetMethod("OnClientFrame", AccessTools.all),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(AnimationPatch), nameof(AnimationPatch.ReplaceAnimator)))
            );

        new Harmony(harmonyId).Patch(
                typeof(EntityPlayer).GetMethod("updateEyeHeight", AccessTools.all),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(EyeHightController), nameof(EyeHightController.UpdateEyeHeight)))
            );
    }

    public static void Unpatch(string harmonyId)
    {
        new Harmony(harmonyId).Unpatch(typeof(EntityShapeRenderer).GetMethod("RenderHeldItem", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
        new Harmony(harmonyId).Unpatch(typeof(EntityShapeRenderer).GetMethod("DoRender3DOpaque", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
        new Harmony(harmonyId).Unpatch(typeof(Vintagestory.API.Common.AnimationManager).GetMethod("OnClientFrame", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
        new Harmony(harmonyId).Unpatch(typeof(EntityPlayer).GetMethod("OnSelfBeforeRender", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
        new Harmony(harmonyId).Unpatch(typeof(EntityPlayer).GetMethod("updateEyeHeight", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
    }

    public static void OnFrameInvoke(Entity entity, ElementPose pose) => OnFrame?.Invoke(entity, pose);

    private static readonly FieldInfo? _entity = typeof(Vintagestory.API.Common.AnimationManager).GetField("entity", BindingFlags.NonPublic | BindingFlags.Instance);

    private static void BeforeRender(EntityPlayer __instance, float dt)
    {
        OnBeforeFrame?.Invoke(__instance, dt);
    }

    private static void DoRender3DOpaque(EntityShapeRenderer __instance, float dt, bool isShadowPass)
    {
        var behavior = __instance.entity.GetBehavior<CollidersEntityBehavior>();
        behavior?.Render(__instance.entity.Api as ICoreClientAPI, __instance.entity as EntityAgent, __instance);
    }

    private static void ReplaceAnimator(Vintagestory.API.Common.AnimationManager __instance, float dt)
    {
        EntityAgent? entity = (Entity?)_entity?.GetValue(__instance) as EntityAgent;

        ClientAnimator? animator = __instance.Animator as ClientAnimator;
        if (__instance.Animator is not ProceduralClientAnimator && animator != null)
        {
            if (entity != null)
            {
                __instance.Animator = ProceduralClientAnimator.Create(__instance, animator, entity);
            }
        }
    }

    private static bool RenderHeldItem(EntityShapeRenderer __instance, float dt, bool isShadowPass, bool right)
    {
        //if (isShadowPass) return true;

        ItemSlot? slot;

        if (right)
        {
            slot = (__instance.entity as EntityPlayer)?.RightHandItemSlot;
        }
        else
        {
            slot = (__instance.entity as EntityPlayer)?.LeftHandItemSlot;
        }

        if (slot?.Itemstack?.Item == null) return true;

        Animatable? behavior = slot.Itemstack.Item.GetCollectibleBehavior(typeof(Animatable), true) as Animatable;

        if (behavior == null) return true;

        ItemRenderInfo renderInfo = __instance.capi.Render.GetItemStackRenderInfo(slot, EnumItemRenderTarget.HandTp, dt);

        behavior.BeforeRender(__instance.capi, slot.Itemstack, __instance.entity, EnumItemRenderTarget.HandFp, dt);

        (string textureName, _) = slot.Itemstack.Item.Textures.First();

        TextureAtlasPosition atlasPos = __instance.capi.ItemTextureAtlas.GetPosition(slot.Itemstack.Item, textureName);

        renderInfo.TextureId = atlasPos.atlasTextureId;

        Vec4f? lightrgbs = (Vec4f?)typeof(EntityShapeRenderer)
                                          .GetField("lightrgbs", BindingFlags.NonPublic | BindingFlags.Instance)
                                          ?.GetValue(__instance);

        behavior.RenderHeldItem(__instance.ModelMat, __instance.capi, slot, __instance.entity, lightrgbs, dt, isShadowPass, right, renderInfo);

        return false;
    }
}


internal delegate void AnimatorEventDelegate(ElementPose pose, ref float weight, Shape shape);

internal class ProceduralClientAnimator : ClientAnimator
{
    static public event AnimatorEventDelegate? WeightCalculation;
    static public event AnimatorEventDelegate? AnimationApplication;
    static public event Action<ShapeElement, Entity, Shape> ShapeElementAnimated;

    public ProceduralClientAnimator(ClientAnimator previous, EntityAgent entity, Vintagestory.API.Common.Animation[] animations, Action<string> onAnimationStoppedListener) : base(
        () => entity.Controls.MovespeedMultiplier * entity.GetWalkSpeedMultiplier(),
        previous.RootPoses,
        animations,
        previous.rootElements,
        previous.jointsById,
        onAnimationStoppedListener
        )
    {
        _entity = entity;
        _colliders = entity.GetBehavior<CollidersEntityBehavior>();

        frameByDepthByAnimation = (List<ElementPose>[][])_frameByDepthByAnimation.GetValue(previous);
        nextFrameTransformsByAnimation = (List<ElementPose>[][])_nextFrameTransformsByAnimation.GetValue(previous);
        weightsByAnimationAndElement = (ShapeElementWeights[][][])_weightsByAnimationAndElement.GetValue(previous);
        prevFrameArray = (int[])((int[])_prevFrame.GetValue(previous)).Clone();
        nextFrameArray = (int[])((int[])_nextFrame.GetValue(previous)).Clone();
        localTransformMatrix = (float[])((float[])_localTransformMatrix.GetValue(previous)).Clone();
        weightsByAnimationAndElement_this = (ShapeElementWeights[][][])((ShapeElementWeights[][][])_weightsByAnimationAndElement_this.GetValue(previous)).Clone();
        tmpMatrix = (float[])((float[])_tmpMatrix.GetValue(previous)).Clone();
    }

    public ProceduralClientAnimator(Shape shape, WalkSpeedSupplierDelegate walkSpeedSupplier, List<ElementPose> rootPoses, Vintagestory.API.Common.Animation[] animations, ShapeElement[] rootElements, Dictionary<int, AnimationJoint> jointsById, Action<string> onAnimationStoppedListener = null) : base(
        walkSpeedSupplier,
        rootPoses,
        animations,
        rootElements,
        jointsById,
        onAnimationStoppedListener
        )
    {
        _entity = null;
        _shape = shape;

        frameByDepthByAnimation = (List<ElementPose>[][])_frameByDepthByAnimation.GetValue(this);
        nextFrameTransformsByAnimation = (List<ElementPose>[][])_nextFrameTransformsByAnimation.GetValue(this);
        weightsByAnimationAndElement = (ShapeElementWeights[][][])_weightsByAnimationAndElement.GetValue(this);
        prevFrameArray = (int[])((int[])_prevFrame.GetValue(this)).Clone();
        nextFrameArray = (int[])((int[])_nextFrame.GetValue(this)).Clone();
        localTransformMatrix = (float[])((float[])_localTransformMatrix.GetValue(this)).Clone();
        weightsByAnimationAndElement_this = (ShapeElementWeights[][][])((ShapeElementWeights[][][])_weightsByAnimationAndElement_this.GetValue(this)).Clone();
        tmpMatrix = (float[])((float[])_tmpMatrix.GetValue(this)).Clone();
    }

    public ProceduralClientAnimator(Shape shape, WalkSpeedSupplierDelegate walkSpeedSupplier, Vintagestory.API.Common.Animation[] animations, ShapeElement[] rootElements, Dictionary<int, AnimationJoint> jointsById, Action<string> onAnimationStoppedListener = null) : base(
        walkSpeedSupplier,
        animations,
        rootElements,
        jointsById,
        onAnimationStoppedListener
        )
    {
        _entity = null;
        _shape = shape;

        frameByDepthByAnimation = (List<ElementPose>[][])_frameByDepthByAnimation.GetValue(this);
        nextFrameTransformsByAnimation = (List<ElementPose>[][])_nextFrameTransformsByAnimation.GetValue(this);
        weightsByAnimationAndElement = (ShapeElementWeights[][][])_weightsByAnimationAndElement.GetValue(this);
        prevFrameArray = (int[])((int[])_prevFrame.GetValue(this)).Clone();
        nextFrameArray = (int[])((int[])_nextFrame.GetValue(this)).Clone();
        localTransformMatrix = (float[])((float[])_localTransformMatrix.GetValue(this)).Clone();
        weightsByAnimationAndElement_this = (ShapeElementWeights[][][])((ShapeElementWeights[][][])_weightsByAnimationAndElement_this.GetValue(this)).Clone();
        tmpMatrix = (float[])((float[])_tmpMatrix.GetValue(this)).Clone();
    }

    public static ProceduralClientAnimator Create(Vintagestory.API.Common.AnimationManager proceduralManager, ClientAnimator previousAnimator, Entity entity)
    {
        WalkSpeedSupplierDelegate? walkSpeedSupplier = (WalkSpeedSupplierDelegate?)_walkSpeedSupplier?.GetValue(previousAnimator);
        Action<string>? onAnimationStoppedListener = (Action<string>?)_onAnimationStoppedListener?.GetValue(previousAnimator);
        Vintagestory.API.Common.Animation[] animations = (Vintagestory.API.Common.Animation[])previousAnimator.anims.Select(entry => entry.Animation).ToArray().Clone();

        ProceduralClientAnimator result = new(previousAnimator, entity as EntityAgent, animations, proceduralManager.OnAnimationStopped);


        return result;
    }

    protected override void calculateMatrices(float dt)
    {
        if (!base.CalculateMatrices)
        {
            return;
        }

        try
        {
            jointsDone.Clear();
            int num = 0;
            for (int i = 0; i < activeAnimCount; i++)
            {
                RunningAnimation runningAnimation = CurAnims[i];
                weightsByAnimationAndElement[0][i] = runningAnimation.ElementWeights;
                num = Math.Max(num, runningAnimation.Animation.Version);
                Vintagestory.API.Common.AnimationFrame[] array = runningAnimation.Animation.PrevNextKeyFrameByFrame[(int)runningAnimation.CurrentFrame % runningAnimation.Animation.QuantityFrames];
                frameByDepthByAnimation[0][i] = array[0].RootElementTransforms;
                prevFrameArray[i] = array[0].FrameNumber;
                if (runningAnimation.Animation.OnAnimationEnd == EnumEntityAnimationEndHandling.Hold && (int)runningAnimation.CurrentFrame + 1 == runningAnimation.Animation.QuantityFrames)
                {
                    nextFrameTransformsByAnimation[0][i] = array[0].RootElementTransforms;
                    nextFrameArray[i] = array[0].FrameNumber;
                }
                else
                {
                    nextFrameTransformsByAnimation[0][i] = array[1].RootElementTransforms;
                    nextFrameArray[i] = array[1].FrameNumber;
                }
            }

            float[] tmp = new float[16];
            Mat4f.Identity(tmp);

            if (_colliders != null) _colliders.Animator = this;

            CalculateMatrices(num, dt, RootPoses, weightsByAnimationAndElement[0], Mat4f.Create(), frameByDepthByAnimation[0], nextFrameTransformsByAnimation[0], 0);

            for (int j = 0; j < GlobalConstants.MaxAnimatedElements; j++)
            {
                if (!jointsById.ContainsKey(j))
                {
                    for (int k = 0; k < 12; k++)
                    {
                        TransformationMatrices4x3[j * 12 + k] = AnimatorBase.identMat4x3[k];
                    }
                }
            }

            foreach (KeyValuePair<string, AttachmentPointAndPose> item in AttachmentPointByCode)
            {
                for (int l = 0; l < 16; l++)
                {
                    item.Value.AnimModelMatrix[l] = item.Value.CachedPose.AnimModelMatrix[l];
                }
            }
        }
        catch (Exception exception)
        {

        }
    }

    internal Entity? _entity;
    internal Shape? _shape;
    internal CollidersEntityBehavior? _colliders;

    private readonly List<ElementPose>[][] frameByDepthByAnimation;
    private readonly List<ElementPose>[][] nextFrameTransformsByAnimation;
    private readonly ShapeElementWeights[][][] weightsByAnimationAndElement;
    private readonly int[] prevFrameArray;
    private readonly int[] nextFrameArray;
    private readonly float[] localTransformMatrix;
    private readonly ShapeElementWeights[][][] weightsByAnimationAndElement_this;
    private readonly float[] tmpMatrix;

    private static readonly FieldInfo? _walkSpeedSupplier = typeof(Vintagestory.API.Common.ClientAnimator).GetField("WalkSpeedSupplier", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? _onAnimationStoppedListener = typeof(Vintagestory.API.Common.ClientAnimator).GetField("onAnimationStoppedListener", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? _frameByDepthByAnimation = typeof(Vintagestory.API.Common.ClientAnimator).GetField("frameByDepthByAnimation", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? _nextFrameTransformsByAnimation = typeof(Vintagestory.API.Common.ClientAnimator).GetField("nextFrameTransformsByAnimation", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? _weightsByAnimationAndElement = typeof(Vintagestory.API.Common.ClientAnimator).GetField("weightsByAnimationAndElement", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? _prevFrame = typeof(Vintagestory.API.Common.ClientAnimator).GetField("prevFrame", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? _nextFrame = typeof(Vintagestory.API.Common.ClientAnimator).GetField("nextFrame", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? _localTransformMatrix = typeof(Vintagestory.API.Common.ClientAnimator).GetField("localTransformMatrix", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
    private static readonly FieldInfo? _weightsByAnimationAndElement_this = typeof(Vintagestory.API.Common.ClientAnimator).GetField("weightsByAnimationAndElement", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
    private static readonly FieldInfo? _activeAnimationCount = typeof(Vintagestory.API.Common.AnimatorBase).GetField("activeAnimCount", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
    private static readonly FieldInfo? _jointsDone = typeof(Vintagestory.API.Common.ClientAnimator).GetField("jointsDone", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
    private static readonly FieldInfo? _tmpMatrix = typeof(Vintagestory.API.Common.ClientAnimator).GetField("tmpMatrix", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);

    private bool CalculateMatrices(
        int animVersion,
        float dt,
        List<ElementPose> outFrame,
        ShapeElementWeights[][] weightsByAnimationAndElement,
        float[] modelMatrix,
        List<ElementPose>[] nowKeyFrameByAnimation,
        List<ElementPose>[] nextInKeyFrameByAnimation,
        int depth
    )
    {
        depth++;
        List<ElementPose>[] nowChildKeyFrameByAnimation = frameByDepthByAnimation[depth];
        List<ElementPose>[] nextChildKeyFrameByAnimation = nextFrameTransformsByAnimation[depth];
        ShapeElementWeights[][] childWeightsByAnimationAndElement = weightsByAnimationAndElement_this[depth];

        for (int childPoseIndex = 0; childPoseIndex < outFrame.Count; childPoseIndex++)
        {
            ElementPose outFramePose = outFrame[childPoseIndex];
            ShapeElement elem = outFramePose.ForElement;

            if (_entity != null && _shape != null) ShapeElementAnimated?.Invoke(elem, _entity, _shape);

            SetMat(outFramePose, modelMatrix);
            Mat4f.Identity(localTransformMatrix);

            outFramePose.Clear();

            float weightSum = SumWeights(childPoseIndex, weightsByAnimationAndElement);
            float weightSumCopy = weightSum;

            if (_shape != null) WeightCalculation?.Invoke(outFramePose, ref weightSum, _shape);

            CalculateAnimationForElements(
                    nowChildKeyFrameByAnimation,
                    nextChildKeyFrameByAnimation,
                    childWeightsByAnimationAndElement,
                    nowKeyFrameByAnimation,
                    nextInKeyFrameByAnimation,
                    weightsByAnimationAndElement,
                    outFramePose,
                    ref weightSum,
                    childPoseIndex
                    );

            if (_shape != null) AnimationApplication?.Invoke(outFramePose, ref weightSumCopy, _shape);

            if (_entity != null) AnimationPatch.OnFrameInvoke(_entity, outFramePose);

            elem.GetLocalTransformMatrix(animVersion, localTransformMatrix, outFramePose);
            Mat4f.Mul(outFramePose.AnimModelMatrix, outFramePose.AnimModelMatrix, localTransformMatrix);
            CalculateElementTransformMatrices(elem, outFramePose);

            _colliders?.SetColliderElement(elem);

            if (outFramePose.ChildElementPoses != null)
            {
                CalculateMatrices(
                    animVersion,
                    dt,
                    outFramePose.ChildElementPoses,
                    childWeightsByAnimationAndElement,
                    outFramePose.AnimModelMatrix,
                    nowChildKeyFrameByAnimation,
                    nextChildKeyFrameByAnimation,
                    depth
                );
            }

        }

        return false;
    }

    private static void SetMat(ElementPose pose, float[] modelMatrix)
    {
        for (int i = 0; i < 16; i++)
        {
            pose.AnimModelMatrix[i] = modelMatrix[i];
        }
    }

    private static void SetMat(float[] from, float[] to)
    {
        for (int i = 0; i < 16; i++)
        {
            to[i] = from[i];
        }
    }

    private float SumWeights(int childPoseIndex, ShapeElementWeights[][] weightsByAnimationAndElement)
    {
        int? activeAnimationCount = (int?)_activeAnimationCount?.GetValue(this); // @TODO replace reflection with something else
        if (activeAnimationCount == null) return 0;

        float weightSum = 0f;
        for (int animationIndex = 0; animationIndex < activeAnimationCount.Value; animationIndex++)
        {
            RunningAnimation animation = CurAnims[animationIndex];
            ShapeElementWeights weight = weightsByAnimationAndElement[animationIndex][childPoseIndex];

            if (weight.BlendMode != EnumAnimationBlendMode.Add)
            {
                weightSum += weight.Weight * animation.EasingFactor;
            }
        }

        return weightSum;
    }

    private void CalculateAnimationForElements(
        List<ElementPose>[] nowChildKeyFrameByAnimation,
        List<ElementPose>[] nextChildKeyFrameByAnimation,
        ShapeElementWeights[][] childWeightsByAnimationAndElement,
        List<ElementPose>[] nowKeyFrameByAnimation,
        List<ElementPose>[] nextInKeyFrameByAnimation,
        ShapeElementWeights[][] weightsByAnimationAndElement,
        ElementPose outFramePose,
        ref float weightSum,
        int childPoseIndex
    )
    {
        int? activeAnimationCount = (int?)_activeAnimationCount?.GetValue(this); // @TODO replace reflection

        if (activeAnimationCount == null || prevFrameArray == null || nextFrameArray == null) return;

        for (int animationIndex = 0; animationIndex < activeAnimationCount.Value; animationIndex++)
        {
            RunningAnimation animation = CurAnims[animationIndex];
            ShapeElementWeights sew = weightsByAnimationAndElement[animationIndex][childPoseIndex];
            CalcBlendedWeight(animation, weightSum / sew.Weight, sew.BlendMode);

            ElementPose nowFramePose = nowKeyFrameByAnimation[animationIndex][childPoseIndex];
            ElementPose nextFramePose = nextInKeyFrameByAnimation[animationIndex][childPoseIndex];

            int prevFrame = prevFrameArray[animationIndex];
            int nextFrame = nextFrameArray[animationIndex];

            // May loop around, so nextFrame can be smaller than prevFrame
            float keyFrameDist = nextFrame > prevFrame ? (nextFrame - prevFrame) : (animation.Animation.QuantityFrames - prevFrame + nextFrame);
            float curFrameDist = animation.CurrentFrame >= prevFrame ? (animation.CurrentFrame - prevFrame) : (animation.Animation.QuantityFrames - prevFrame + animation.CurrentFrame);

            float lerp = curFrameDist / keyFrameDist;

            outFramePose.Add(nowFramePose, nextFramePose, lerp, animation.BlendedWeight);

            nowChildKeyFrameByAnimation[animationIndex] = nowFramePose.ChildElementPoses;
            childWeightsByAnimationAndElement[animationIndex] = sew.ChildElements;

            nextChildKeyFrameByAnimation[animationIndex] = nextFramePose.ChildElementPoses;
        }
    }

    private static void CalcBlendedWeight(RunningAnimation animation, float weightSum, EnumAnimationBlendMode blendMode)
    {
        if (weightSum == 0f)
        {
            animation.BlendedWeight = animation.EasingFactor;
        }
        else
        {
            animation.BlendedWeight = GameMath.Clamp((blendMode == EnumAnimationBlendMode.Add) ? animation.EasingFactor : (animation.EasingFactor / Math.Max(animation.meta.WeightCapFactor, weightSum)), 0f, 1f);
        }
    }

    private void CalculateElementTransformMatrices(ShapeElement element, ElementPose pose)
    {
        if (jointsDone == null || tmpMatrix == null) return;

        if (element.JointId > 0 && !jointsDone.Contains(element.JointId))
        {
            Mat4f.Mul(tmpMatrix, pose.AnimModelMatrix, element.inverseModelTransform);

            int index = 12 * element.JointId;
            TransformationMatrices4x3[index++] = tmpMatrix[0];
            TransformationMatrices4x3[index++] = tmpMatrix[1];
            TransformationMatrices4x3[index++] = tmpMatrix[2];
            TransformationMatrices4x3[index++] = tmpMatrix[4];
            TransformationMatrices4x3[index++] = tmpMatrix[5];
            TransformationMatrices4x3[index++] = tmpMatrix[6];
            TransformationMatrices4x3[index++] = tmpMatrix[8];
            TransformationMatrices4x3[index++] = tmpMatrix[9];
            TransformationMatrices4x3[index++] = tmpMatrix[10];
            TransformationMatrices4x3[index++] = tmpMatrix[12];
            TransformationMatrices4x3[index++] = tmpMatrix[13];
            TransformationMatrices4x3[index] = tmpMatrix[14];

            jointsDone.Add(element.JointId);
        }
    }
}