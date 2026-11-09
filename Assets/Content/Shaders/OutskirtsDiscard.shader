Shader "Hidden/Client/OutskirtsDiscard"
{
    SubShader
    {
        Tags { "Queue" = "Transparent" }
        Pass
        {
            ColorMask 0
            ZWrite Off
        }
    }
}
