using CombatOverhaul.Utils;
using Vintagestory.API.Common;

namespace CombatOverhaul.Animations;

public delegate bool AnimationSpeedModifierDelegate(TimeSpan duration, ref TimeSpan delta);

public sealed class Composer
{
    public Composer(SoundsSynchronizerClient? soundsManager, ParticleEffectsManager? particleEffectsManager, EntityPlayer player)
    {
        _soundsManager = soundsManager;
        _particleEffectsManager = particleEffectsManager;
        _player = player;
    }

    public PlayerItemFrame Compose(TimeSpan delta)
    {
        while (_requestsQueue.Any())
        {
            AnimationRequest request = _requestsQueue.Dequeue();
            ProcessRequest(request);
        }

        if (_speedModifierDelegate != null)
        {
            _speedModifierDuration += delta;
            if (!_speedModifierDelegate.Invoke(_speedModifierDuration, ref delta))
            {
                _speedModifierDelegate = null;
            }
        }

        if (!_requests.Any() || !_animators.Any()) return PlayerItemFrame.Empty;

        foreach ((string category, AnimatorWeightState state) in _weightState)
        {
            _currentTimes[category] += delta;
            ProcessWeight(category, state);
        }

        List<(PlayerItemFrame, float)> frames = new();
        foreach ((string category, Animator? animator) in _animators)
        {
            PlayerItemFrame frame = animator.Animate(delta, out IEnumerable<string> callbacks);
            frames.Add((frame, _currentWeight[category]));

            foreach (string callbackId in callbacks)
            {
                _requests[category].CallbackHandler?.Invoke(callbackId);
            }
        }
        PlayerItemFrame result = PlayerItemFrame.Compose(frames);

        foreach (string category in _requests.Select(entry => entry.Key).ToArray())
        {
            if ((_animators[category].Finished() && _weightState[category] == AnimatorWeightState.Finished))
            {
                System.Func<bool>? callback = _requests[category].FinishCallback;
                bool removeCategory = true;
                if (callback != null && !_callbacksCalled[category])
                {
                    removeCategory = !callback.Invoke();
                    _callbacksCalled[category] = true;
                }

                if (removeCategory)
                {
                    _animators.Remove(category);
                    _currentTimes.Remove(category);
                    _previousWeight.Remove(category);
                    _currentWeight.Remove(category);
                    _weightState.Remove(category);
                    _requests.Remove(category);
                    _callbacksCalled.Remove(category);

                    continue;
                }
            }

            if (_animators[category].Stopped() && _requests[category].FinishCallback != null)
            {
                System.Func<bool>? callback = _requests[category].FinishCallback;
                bool removeCategory = false;
                if (callback != null && !_callbacksCalled[category])
                {
                    removeCategory = !callback.Invoke();
                    _callbacksCalled[category] = true;
                }

                if (removeCategory)
                {
                    _animators.Remove(category);
                    _currentTimes.Remove(category);
                    _previousWeight.Remove(category);
                    _currentWeight.Remove(category);
                    _weightState.Remove(category);
                    _requests.Remove(category);
                    _callbacksCalled.Remove(category);

                    continue;
                }
            }
        }

        return result;
    }

    public void Play(AnimationRequest request)
    {
        _requestsQueue.Enqueue(request);
    }

    public void Stop(string category)
    {
        _animators.Remove(category);
        _currentTimes.Remove(category);
        _previousWeight.Remove(category);
        _currentWeight.Remove(category);
        _weightState.Remove(category);
        _requests.Remove(category);
        _callbacksCalled.Remove(category);
    }

    public void StopAll()
    {
        _animators.Clear();
        _currentTimes.Clear();
        _previousWeight.Clear();
        _currentWeight.Clear();
        _weightState.Clear();
        _requests.Clear();
        _callbacksCalled.Clear();
    }

    public bool AnyActiveAnimations() => _animators.Any();

    public void SetSpeedModifier(AnimationSpeedModifierDelegate modifier)
    {
        _speedModifierDuration = TimeSpan.Zero;
        _speedModifierDelegate = modifier;
    }

    public void StopSpeedModifier()
    {
        _speedModifierDelegate = null;
    }

    public bool IsSpeedModifierActive() => _speedModifierDelegate != null;

    private enum AnimatorWeightState
    {
        EaseIn,
        Stay,
        EaseOut,
        Finished
    }

    private readonly Dictionary<string, Animator> _animators = new();
    private readonly Dictionary<string, AnimationRequest> _requests = new();
    private readonly Dictionary<string, float> _previousWeight = new();
    private readonly Dictionary<string, float> _currentWeight = new();
    private readonly Dictionary<string, AnimatorWeightState> _weightState = new();
    private readonly Dictionary<string, TimeSpan> _currentTimes = new();
    private readonly Dictionary<string, bool> _callbacksCalled = new();
    private readonly Queue<AnimationRequest> _requestsQueue = new();
    private readonly SoundsSynchronizerClient? _soundsManager;
    private readonly ParticleEffectsManager? _particleEffectsManager;
    private readonly EntityPlayer _player;
    private TimeSpan _speedModifierDuration = TimeSpan.Zero;
    private AnimationSpeedModifierDelegate? _speedModifierDelegate;

    private void ProcessWeight(string category, AnimatorWeightState state)
    {
        switch (state)
        {
            case AnimatorWeightState.EaseIn:
                float progress = Math.Clamp((float)(_currentTimes[category] / _requests[category].EaseInDuration), 0, 1);
                _currentWeight[category] = _previousWeight[category] + (_requests[category].Weight - _previousWeight[category]) * progress;
                if (progress >= 1)
                {
                    _currentWeight[category] = _requests[category].Weight;
                    _weightState[category] = AnimatorWeightState.Stay;
                }
                break;
            case AnimatorWeightState.Stay:
                if (_requests[category].EaseOut && _animators[category].Finished()/*_requests[category].Animation.TotalDuration / _requests[category].AnimationSpeed >= _currentTimes[category]*/)
                {
                    _weightState[category] = AnimatorWeightState.EaseOut;
                }
                break;
            case AnimatorWeightState.EaseOut:
                float progress2 = Math.Clamp((float)((_currentTimes[category] - _requests[category].Animation.TotalDuration / _requests[category].AnimationSpeed) / _requests[category].EaseOutDuration), 0, 1);
                _currentWeight[category] = _requests[category].Weight * (1f - progress2);
                if (progress2 >= 1)
                {
                    _currentWeight[category] = 0;
                    _weightState[category] = AnimatorWeightState.Finished;
                }
                break;
        }
    }
    private void ProcessRequest(AnimationRequest request)
    {
        if (_animators.ContainsKey(request.Category))
        {
            string category = request.Category;
            _animators[category].Play(request.Animation, request.AnimationSpeed);
            _requests[category] = request;
            _previousWeight[category] = _currentWeight[category];
            _weightState[category] = AnimatorWeightState.EaseIn;
            _currentTimes[category] = TimeSpan.Zero;
            _callbacksCalled[category] = false;
        }
        else
        {
            string category = request.Category;
            _animators.Add(category, new Animator(request.Animation, _soundsManager, _particleEffectsManager, _player, request.AnimationSpeed));
            _requests.Add(category, request);
            _previousWeight[category] = 0;
            _currentWeight[category] = 0;
            _weightState[category] = AnimatorWeightState.EaseIn;
            _currentTimes[category] = TimeSpan.Zero;
            _callbacksCalled[category] = false;
        }
    }
}

public readonly struct AnimationRequest
{
    public readonly Animation Animation;
    public readonly float AnimationSpeed;
    public readonly float Weight;
    public readonly string Category;
    public readonly TimeSpan EaseOutDuration;
    public readonly TimeSpan EaseInDuration;
    public readonly bool EaseOut;
    public readonly Action<string>? CallbackHandler;
    public readonly System.Func<bool>? FinishCallback;

    public AnimationRequest(Animation animation, float animationSpeed, float weight, string category, TimeSpan easeOutDuration, TimeSpan easeInDuration, bool easeOut, System.Func<bool>? finishCallback = null, Action<string>? callbackHandler = null)
    {
        Animation = animation;
        AnimationSpeed = animationSpeed;
        Weight = weight;
        Category = category;
        EaseOutDuration = easeOutDuration;
        EaseInDuration = easeInDuration;
        EaseOut = easeOut;
        FinishCallback = finishCallback;
        CallbackHandler = callbackHandler;
    }

    public AnimationRequest(Animation animation, AnimationRequestByCode request)
    {
        Animation = animation;
        AnimationSpeed = request.AnimationSpeed;
        Weight = request.Weight;
        Category = request.Category;
        EaseOutDuration = request.EaseOutDuration;
        EaseInDuration = request.EaseInDuration;
        EaseOut = request.EaseOut;
        FinishCallback = request.FinishCallback;
        CallbackHandler = request.CallbackHandler;
    }

    public AnimationRequest(System.Func<bool> callback, AnimationRequest request)
    {
        Animation = request.Animation;
        AnimationSpeed = request.AnimationSpeed;
        Weight = request.Weight;
        Category = request.Category;
        EaseOutDuration = request.EaseOutDuration;
        EaseInDuration = request.EaseInDuration;
        EaseOut = request.EaseOut;
        FinishCallback = callback;
        CallbackHandler = request.CallbackHandler;
    }

    public AnimationRequest()
    {
        Animation = Animation.Zero;
        Category = "";
    }
}

public readonly struct AnimationRequestByCode
{
    public readonly string Animation;
    public readonly float AnimationSpeed;
    public readonly float Weight;
    public readonly string Category;
    public readonly TimeSpan EaseOutDuration;
    public readonly TimeSpan EaseInDuration;
    public readonly bool EaseOut;
    public readonly Action<string>? CallbackHandler;
    public readonly System.Func<bool>? FinishCallback;

    public AnimationRequestByCode(string animation, float animationSpeed, float weight, string category, TimeSpan easeOutDuration, TimeSpan easeInDuration, bool easeOut, System.Func<bool>? finishCallback = null, Action<string>? callbackHandler = null)
    {
        Animation = animation;
        AnimationSpeed = animationSpeed;
        Weight = weight;
        Category = category;
        EaseOutDuration = easeOutDuration;
        EaseInDuration = easeInDuration;
        EaseOut = easeOut;
        FinishCallback = finishCallback;
        CallbackHandler = callbackHandler;
    }
}

internal class Animator
{
    public Animator(Animation animation, SoundsSynchronizerClient? soundsManager, ParticleEffectsManager? particleEffectsManager, EntityPlayer player, float animationSpeed)
    {
        _currentAnimation = animation;
        _soundsManager = soundsManager;
        _animationSpeed = animationSpeed;
        _player = player;
        _particleEffectsManager = particleEffectsManager;
    }

    public bool FinishOverride { get; set; } = false;

    public void Play(Animation animation, TimeSpan duration) => Play(animation, (float)(animation.TotalDuration / duration));
    public void Play(Animation animation, float animationSpeed)
    {
        _currentAnimation = animation;
        _animationSpeed = animationSpeed;
        _currentDuration = TimeSpan.Zero;
        _previousAnimationFrame = _lastFrame;
    }

    public PlayerItemFrame Animate(TimeSpan delta, out IEnumerable<string> callbacks)
    {
        TimeSpan previousDuration = _currentDuration * _animationSpeed;
        _currentDuration += delta;
        TimeSpan adjustedDuration = _currentDuration * _animationSpeed;

        if (_soundsManager != null) _currentAnimation.PlaySounds(_soundsManager, previousDuration, adjustedDuration);
        if (_particleEffectsManager != null) _currentAnimation.SpawnParticles(_player, _particleEffectsManager, previousDuration, adjustedDuration);
        callbacks = _currentAnimation.GetCallbacks(previousDuration, adjustedDuration);

        _lastFrame = _currentAnimation.Interpolate(_previousAnimationFrame, adjustedDuration);
        return _lastFrame;
    }
    public bool Stopped() => _currentAnimation.TotalDuration <= _currentDuration * _animationSpeed;
    public bool Finished() => FinishOverride || (Stopped() && !_currentAnimation.Hold);

    private PlayerItemFrame _previousAnimationFrame = PlayerItemFrame.Zero;
    private PlayerItemFrame _lastFrame = PlayerItemFrame.Zero;
    private TimeSpan _currentDuration = TimeSpan.Zero;
    private float _animationSpeed;
    private Animation _currentAnimation;
    private readonly SoundsSynchronizerClient? _soundsManager;
    private readonly ParticleEffectsManager? _particleEffectsManager;
    private readonly EntityPlayer _player;
}
