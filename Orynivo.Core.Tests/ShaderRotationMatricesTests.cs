using Orynivo.Visualization;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Covers Milkdrop's non-square <c>rot_*</c> matrices. The engine never materialises the
/// <c>float4x3</c> type; it rewrites the two constructs real presets use onto three column uniforms,
/// so the rewrite and the column values are what have to stay correct.
/// </summary>
public sealed class ShaderRotationMatricesTests
{
    /// <summary>A component read becomes a column component.</summary>
    [Theory]
    [InlineData("float r = rot_d1[1].y;", "float r = rot_d1_c1[1];")]
    [InlineData("float r = rot_s3[0].x;", "float r = rot_s3_c0[0];")]
    [InlineData("float r = rot_uf4[2].z;", "float r = rot_uf4_c2[2];")]
    [InlineData("float r = rot_rand1[3].w;", "float r = rot_rand1_c3[3];")]
    [InlineData("float r = rot_f2[1].g;", "float r = rot_f2_c1[1];")]
    public void Rewrite_TurnsComponentReadsIntoColumns(string source, string expected) =>
        Assert.Equal(expected, ShaderRotationMatrices.Rewrite(source));

    /// <summary>A row-vector product becomes the column helper call.</summary>
    [Fact]
    public void Rewrite_TurnsProductIntoColumns() =>
        Assert.Equal(
            "uv = orynivo_mul4x3(uv, rot_d2_c0, rot_d2_c1, rot_d2_c2);",
            ShaderRotationMatrices.Rewrite("uv = mul(uv, rot_d2);"));

    /// <summary>A negated matrix flips every column.</summary>
    [Fact]
    public void Rewrite_TurnsNegatedProductIntoColumns() =>
        Assert.Equal(
            "sp = orynivo_mul4x3(sp, -rot_d2_c0, -rot_d2_c1, -rot_d2_c2);",
            ShaderRotationMatrices.Rewrite("sp = mul(sp, -rot_d2);"));

    /// <summary>A product whose first argument is a call keeps that argument intact.</summary>
    [Fact]
    public void Rewrite_KeepsANestedFirstArgument() =>
        Assert.Equal(
            "st = orynivo_mul4x3(cos(st), rot_d1_c0, rot_d1_c1, rot_d1_c2);",
            ShaderRotationMatrices.Rewrite("st = mul(cos(st), rot_d1);"));

    /// <summary>A commented use is rewritten without breaking the rest of the source.</summary>
    [Fact]
    public void Rewrite_HandlesCommentedUses() =>
        Assert.Equal(
            "//uvw = orynivo_mul4x3(uvw, rot_s2_c0, rot_s2_c1, rot_s2_c2);\nuv = uv * 2;",
            ShaderRotationMatrices.Rewrite("//uvw = mul(uvw, rot_s2);\nuv = uv * 2;"));

    /// <summary>Text without a matrix is returned unchanged.</summary>
    [Fact]
    public void Rewrite_LeavesOtherSourceAlone()
    {
        const string source = "uv = uv * float2(1.01, 0.99) + 0.5;\nret = tex2D(sampler_main, uv).rgb;";

        Assert.Equal(source, ShaderRotationMatrices.Rewrite(source));
    }

    /// <summary>An unknown matrix-like name is left for the parser to reject as it did before.</summary>
    [Fact]
    public void Rewrite_DoesNotInventUnknownMatrices() =>
        Assert.Equal("float r = rot_z9[1].y;", ShaderRotationMatrices.Rewrite("float r = rot_z9[1].y;"));

    /// <summary>The columns are finite, reproducible for a seed, and differ between matrices.</summary>
    [Fact]
    public void Build_ProducesReproducibleFiniteColumns()
    {
        var first = new ShaderRotationMatrices(12345);
        first.Build(2.5d);
        var firstColumns = ReadColumns(first);

        var second = new ShaderRotationMatrices(12345);
        second.Build(2.5d);
        var secondColumns = ReadColumns(second);

        Assert.Equal(firstColumns, secondColumns);
        Assert.All(firstColumns, value => Assert.True(float.IsFinite(value)));
        Assert.NotEqual(firstColumns[0], firstColumns[1]);
    }

    /// <summary>The four <c>rot_rand</c> matrices change between frames while the others hold still.</summary>
    [Fact]
    public void Build_OnlyRerandomisesTheRandomMatrices()
    {
        var matrices = new ShaderRotationMatrices(7);
        matrices.Build(1d);
        var first = ReadColumns(matrices);
        matrices.Build(1d);
        var second = ReadColumns(matrices);

        // rot_s1 starts at index zero; rot_rand1 starts at matrix twenty.
        Assert.Equal(first[0], second[0]);
        var randomOffset = Array.IndexOf(ShaderRotationMatrices.Names, "rot_rand1") * 12;
        Assert.NotEqual(first[randomOffset], second[randomOffset]);
    }

    /// <summary>A preset whose shader uses the matrices still yields its shader.</summary>
    [Fact]
    public void Parse_KeepsAShaderThatUsesRotationMatrices()
    {
        const string text = """
            [preset00]
            fDecay=0.98
            warp_1=`shader_body
            warp_2=`{
            warp_3=`  uv = mul(uv, rot_d2);
            warp_4=`  ret = tex2D(sampler_main, uv).rgb * rot_d1[1].y;
            warp_5=`}
            """;

        var preset = VisualizerPreset.Parse(text, "RotTest");

        Assert.Single(preset.WarpShaders);
        Assert.Empty(preset.FailedBlocks);
        Assert.True(ContainsText(preset.WarpShaders[0].Program, "rot_d1_c1"));
        Assert.True(ContainsText(preset.WarpShaders[0].Program, "orynivo_mul4x3"));
    }

    private static bool ContainsText(ShaderNode node, string text)
    {
        if (node.Text == text)
            return true;

        if (node.Left is { } left && ContainsText(left, text))
            return true;
        if (node.Right is { } right && ContainsText(right, text))
            return true;
        if (node.Third is { } third && ContainsText(third, text))
            return true;

        foreach (var child in node.Items)
        {
            if (ContainsText(child, text))
                return true;
        }

        return false;
    }

    private static float[] ReadColumns(ShaderRotationMatrices matrices)
    {
        var values = new float[ShaderRotationMatrices.Names.Length * 3 * 4];
        var index = 0;
        foreach (var name in ShaderRotationMatrices.Names)
        {
            for (var column = 0; column < 3; column++)
            {
                var value = matrices.Column(name, column);
                for (var component = 0; component < 4; component++)
                    values[index++] = value.Get(component);
            }
        }

        return values;
    }
}
