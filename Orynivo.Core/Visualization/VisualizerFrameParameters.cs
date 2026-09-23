namespace Orynivo.Visualization;

/// <summary>One of Milkdrop's two border bands: where it sits, how thick it is, and its colour.</summary>
/// <param name="Inset">Inset as a fraction of the smaller frame dimension.</param>
/// <param name="Thickness">Band thickness as a fraction of the smaller frame dimension.</param>
/// <param name="Red">Red, zero to one.</param>
/// <param name="Green">Green, zero to one.</param>
/// <param name="Blue">Blue, zero to one.</param>
/// <param name="Alpha">Blend weight, zero to one.</param>
public readonly record struct VisualizerBorderBand(
    float Inset,
    float Thickness,
    float Red,
    float Green,
    float Blue,
    float Alpha);

/// <summary>
/// The per-frame values the frame passes use, read once after the preset's per-frame block has run.
/// A GPU pipeline reads them instead of duplicating the key lookups and clamps, so the two paths
/// cannot disagree about what a preset asked for.
/// </summary>
/// <param name="Decay">Feedback decay, zero to one.</param>
/// <param name="BlurPasses">Number of box-blur passes over the warped frame.</param>
/// <param name="DarkenCenter">Centre-darkening amount, zero to one.</param>
/// <param name="Gamma">Gamma adjustment, 0.1 to 10.</param>
/// <param name="EchoZoom">Video-echo zoom, 0.1 to 4.</param>
/// <param name="EchoAlpha">Video-echo blend weight, zero to one.</param>
/// <param name="EchoOrientation">Video-echo orientation, zero to three.</param>
/// <param name="OuterBorder">Outer border band.</param>
/// <param name="InnerBorder">Inner border band.</param>
/// <param name="WarpTime">Warp animation time in seconds, which drives the time-dependent warp displacement.</param>
public readonly record struct VisualizerFrameParameters(
    float Decay,
    int BlurPasses,
    float DarkenCenter,
    float Gamma,
    float EchoZoom,
    float EchoAlpha,
    int EchoOrientation,
    VisualizerBorderBand OuterBorder,
    VisualizerBorderBand InnerBorder,
    float WarpTime);
