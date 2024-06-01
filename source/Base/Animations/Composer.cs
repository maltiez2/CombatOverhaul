namespace CombatOverhaul.PlayerAnimations;

internal sealed class Composer
{
    public Composer()
    {

    }

    public PlayerItemFrame Compose(TimeSpan delta)
    {
        if (!_requests.Any()) return PlayerItemFrame.Empty;

        foreach ((string category, AnimatorWeightState state) in _weightState)
        {
            _currentTimes.Add(category, delta);

            ProcessWeight(category, state);
        }

        PlayerItemFrame result = PlayerItemFrame.Compose(_animators.Select(entry => (entry.Value.Animate(delta), _currentWeight[entry.Key])));

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
                _currentWeight[category] = (_requests[category].Weight - _previousWeight[category]) * (float)(_currentTimes[category] / _requests[category].EaseInDuration);
                if (_currentWeight[category] > _requests[category].Weight)
                {
                    _currentWeight[category] = _requests[category].Weight;
                    _weightState[category] = AnimatorWeightState.Stay;
                }
                break;
            case AnimatorWeightState.Stay:
                if (_requests[category].EaseOut && _requests[category].Animation.TotalDuration / _requests[category].AnimationSpeed > _currentTimes[category])
                {
                    _weightState[category] = AnimatorWeightState.EaseOut;
                }
                break;
            case AnimatorWeightState.EaseOut:
                _currentWeight[category] = _requests[category].Weight * (float)((_requests[category].EaseOutDuration - (_currentTimes[category] - _requests[category].Animation.TotalDuration / _requests[category].AnimationSpeed)) / _requests[category].EaseOutDuration);
                if (_currentWeight[category] < 0)
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

internal struct Animator
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
    public readonly bool Finished() => _currentAnimation.TotalDuration >= _currentDuration / _animationSpeed;

    private PlayerItemFrame _previousAnimationFrame = new();
    private PlayerItemFrame _lastFrame = new();
    private TimeSpan _currentDuration = TimeSpan.Zero;
    private float _animationSpeed = 1;
    private Animation _currentAnimation;
}
