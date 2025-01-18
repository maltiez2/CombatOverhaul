using Vintagestory.API.MathTools;

namespace CombatOverhaul.Utils;

static class VectorsUtils
{
    public static System.Numerics.Vector2 toSystem(this OpenTK.Mathematics.Vector2 value) => new(value.X, value.Y);
    public static OpenTK.Mathematics.Vector2 toOpenTK(this System.Numerics.Vector2 value) => new(value.X, value.Y);

    public static System.Numerics.Vector3 toSystem(this OpenTK.Mathematics.Vector3 value) => new(value.X, value.Y, value.Z);
    public static OpenTK.Mathematics.Vector3 toOpenTK(this System.Numerics.Vector3 value) => new(value.X, value.Y, value.Z);

    public static System.Numerics.Vector4 toSystem(this OpenTK.Mathematics.Vector4 value) => new(value.X, value.Y, value.Z, value.W);
    public static OpenTK.Mathematics.Vector4 toOpenTK(this System.Numerics.Vector4 value) => new(value.X, value.Y, value.Z, value.W);

    public static System.Numerics.Vector2 toSystem(this OpenTK.Mathematics.Vector2d value) => new((float)value.X, (float)value.Y);
    public static System.Numerics.Vector3 toSystem(this OpenTK.Mathematics.Vector3d value) => new((float)value.X, (float)value.Y, (float)value.Z);
    public static System.Numerics.Vector4 toSystem(this OpenTK.Mathematics.Vector4d value) => new((float)value.X, (float)value.Y, (float)value.Z, (float)value.W);
}

public class Matrixd
{
    public double[] Values;

    public double[] ValuesAsDouble
    {
        get
        {
            double[] values = new double[16];
            for (int i = 0; i < 16; i++) values[i] = Values[i];
            return values;
        }
    }

    public Matrixd()
    {
        Values = Mat4d.Create();
    }

    public Matrixd(double[] values)
    {
        Values = Mat4d.Create();
        Set(values);
    }

    public static Matrixd Create()
    {
        return new Matrixd();
    }

    public Matrixd Identity()
    {
        Mat4d.Identity(Values);
        return this;
    }

    public Matrixd Set(double[] values)
    {
        Values[0] = values[0];
        Values[1] = values[1];
        Values[2] = values[2];
        Values[3] = values[3];
        Values[4] = values[4];
        Values[5] = values[5];
        Values[6] = values[6];
        Values[7] = values[7];
        Values[8] = values[8];
        Values[9] = values[9];
        Values[10] = values[10];
        Values[11] = values[11];
        Values[12] = values[12];
        Values[13] = values[13];
        Values[14] = values[14];
        Values[15] = values[15];
        return this;
    }

    public Matrixd Set(float[] values)
    {
        Values[0] = (double)values[0];
        Values[1] = (double)values[1];
        Values[2] = (double)values[2];
        Values[3] = (double)values[3];
        Values[4] = (double)values[4];
        Values[5] = (double)values[5];
        Values[6] = (double)values[6];
        Values[7] = (double)values[7];
        Values[8] = (double)values[8];
        Values[9] = (double)values[9];
        Values[10] = (double)values[10];
        Values[11] = (double)values[11];
        Values[12] = (double)values[12];
        Values[13] = (double)values[13];
        Values[14] = (double)values[14];
        Values[15] = (double)values[15];
        return this;
    }

    public Matrixd Translate(double x, double y, double z)
    {
        Mat4d.Translate(Values, Values, (double)x, (double)y, (double)z);
        return this;
    }

    public Matrixd Translate(Vec3f vec)
    {
        Mat4d.Translate(Values, Values, vec.X, vec.Y, vec.Z);
        return this;
    }


    public Matrixd Translate(float x, float y, float z)
    {
        Mat4d.Translate(Values, Values, x, y, z);
        return this;
    }

    public Matrixd Scale(double x, double y, double z)
    {
        Mat4d.Scale(Values, Values, new double[] { x, y, z });
        return this;
    }

    public Matrixd RotateDeg(Vec3f degrees)
    {
        Mat4d.RotateX(Values, Values, degrees.X * GameMath.DEG2RAD);
        Mat4d.RotateY(Values, Values, degrees.Y * GameMath.DEG2RAD);
        Mat4d.RotateZ(Values, Values, degrees.Z * GameMath.DEG2RAD);
        return this;
    }


    public Matrixd Rotate(Vec3f radians)
    {
        Mat4d.RotateX(Values, Values, radians.X);
        Mat4d.RotateY(Values, Values, radians.Y);
        Mat4d.RotateZ(Values, Values, radians.Z);
        return this;
    }

    public Matrixd Rotate(double radX, double radY, double radZ)
    {
        Mat4d.RotateX(Values, Values, radX);
        Mat4d.RotateY(Values, Values, radY);
        Mat4d.RotateZ(Values, Values, radZ);
        return this;
    }

    public Matrixd RotateX(double radX)
    {
        Mat4d.RotateX(Values, Values, radX);
        return this;
    }

    public Matrixd RotateY(double radY)
    {
        Mat4d.RotateY(Values, Values, radY);
        return this;
    }

    public Matrixd RotateZ(double radZ)
    {
        Mat4d.RotateZ(Values, Values, radZ);
        return this;
    }





    public Matrixd RotateXDeg(double degX)
    {
        Mat4d.RotateX(Values, Values, degX * GameMath.DEG2RAD);
        return this;
    }

    public Matrixd RotateYDeg(double degY)
    {
        Mat4d.RotateY(Values, Values, degY * GameMath.DEG2RAD);
        return this;
    }

    public Matrixd RotateZDeg(double degZ)
    {
        Mat4d.RotateZ(Values, Values, degZ * GameMath.DEG2RAD);
        return this;
    }


    public Vec4d TransformVector(Vec4d vec)
    {
        Vec4d outval = new Vec4d();
        Mat4d.MulWithVec4(Values, vec, outval);
        return outval;
    }


    public Matrixd Mul(double[] matrix)
    {
        Mat4d.Mul(Values, Values, matrix);
        return this;
    }

    public Matrixd Mul(Matrixd matrix)
    {
        Mat4d.Mul(Values, Values, matrix.Values);
        return this;
    }

    public Matrixd ReverseMul(double[] matrix)
    {
        Mat4d.Mul(Values, matrix, Values);
        return this;
    }

    public Matrixd FollowPlayer()
    {
        Values[12] = 0;
        Values[13] = 0;
        Values[14] = 0;
        return this;
    }

    public Matrixd FollowPlayerXZ()
    {
        Values[12] = 0;
        Values[14] = 0;
        return this;
    }

    public Matrixd Invert()
    {
        Mat4d.Invert(Values, Values);
        return this;
    }

    public Matrixd Clone()
    {
        return new Matrixd()
        {
            Values = (double[])Values.Clone()
        };
    }

}
