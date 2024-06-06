namespace CombatOverhaul.Animations;

internal sealed class Composer
{
    public Composer()
    {

    }

    public PlayerItemFrame Compose(TimeSpan delta)
    {
        if (!_requests.Any() || !_animators.Any()) return PlayerItemFrame.Empty;

        foreach ((string category, AnimatorWeightState state) in _weightState)
        {
            _currentTimes[category] += delta;

            ProcessWeight(category, state);
        }

        List<(PlayerItemFrame, float)> frames = new();
        foreach ((string category, Animator? animator) in _animators)
        {
            PlayerItemFrame frame = animator.Animate(delta);
            frames.Add((frame, _currentWeight[category]));
        }
        PlayerItemFrame result = PlayerItemFrame.Compose(frames);

        foreach (string category in _requests.Select(entry => entry.Key))
        {
            if (_animators[category].Finished() && _weightState[category] == AnimatorWeightState.Finished)
            {
                _animators.Remove(category);
                _currentTimes.Remove(category);
                _previousWeight.Remove(category);
                _currentWeight.Remove(category);
                _weightState.Remove(category);
                _requests.Remove(category);
            }
        }

        return result;
    }

    public void Play(AnimationRequest request)
    {
        if (_animators.ContainsKey(request.Category))
        {
            string category = request.Category;
            _animators[category].Play(request.Animation, request.AnimationSpeed);
            _requests[category] = request;
            _previousWeight[category] = _currentWeight[category];
            _weightState[category] = AnimatorWeightState.EaseIn;
            _currentTimes[category] = TimeSpan.Zero;
        }
        else
        {
            string category = request.Category;
            _animators.Add(category, new Animator(request.Animation));
            _requests.Add(category, request);
            _previousWeight[category] = 0;
            _currentWeight[category] = 0;
            _weightState[category] = AnimatorWeightState.EaseIn;
            _currentTimes[category] = TimeSpan.Zero;
        }
    }

    public void Stop(string category)
    {
        _animators.Remove(category);
        _currentTimes.Remove(category);
        _previousWeight.Remove(category);
        _currentWeight.Remove(category);
        _weightState.Remove(category);
        _requests.Remove(category);
    }

    public bool AnyActiveAnimations() => _animators.Any();

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
                if (_requests[category].EaseOut && _requests[category].Animation.TotalDuration / _requests[category].AnimationSpeed >= _currentTimes[category])
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

    public AnimationRequest(Animation animation, float animationSpeed, float weight, string category, TimeSpan easeOutDuration, TimeSpan easeInDuration, bool easeOut)
    {
        Animation = animation;
        AnimationSpeed = animationSpeed;
        Weight = weight;
        Category = category;
        EaseOutDuration = easeOutDuration;
        EaseInDuration = easeInDuration;
        EaseOut = easeOut;
    }
}

internal class Animator
{
    public Animator(Animation animation)
    {
        _currentAnimation = animation;
    }

    public void Play(Animation animation, TimeSpan duration) => Play(animation, (float)(animation.TotalDuration / duration));
    public void Play(Animation animation, float animationSpeed)
    {
        _currentAnimation = animation;
        _animationSpeed = animationSpeed;
        _currentDuration = TimeSpan.Zero;
        _previousAnimationFrame = _lastFrame;
    }

    public PlayerItemFrame Animate(TimeSpan delta)
    {
        _currentDuration += delta;
        TimeSpan adjustedDuration = _currentDuration / _animationSpeed;

        _lastFrame = _currentAnimation.Interpolate(_previousAnimationFrame, adjustedDuration);
        return _lastFrame;
    }
    public bool Finished() => (_currentAnimation.TotalDuration <= _currentDuration / _animationSpeed) && !_currentAnimation.Hold;

    private PlayerItemFrame _previousAnimationFrame = PlayerItemFrame.Zero;
    private PlayerItemFrame _lastFrame = PlayerItemFrame.Zero;
    private TimeSpan _currentDuration = TimeSpan.Zero;
    private float _animationSpeed = 1;
    private Animation _currentAnimation;
}
