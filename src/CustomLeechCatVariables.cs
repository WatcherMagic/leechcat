namespace SlugTemplate;

public class CustomLeechCatVariables
{
    public bool beingDrained = false;
    public float exhaustion = 0.0f;
    public bool IsExhausted
    {
        get => exhaustion >= 1.0f;
    }

    public float leechPoison = 0.0f;
}