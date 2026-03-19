/// <summary>
/// Plain-language math snippets for the in-game reader (TextMeshPro rich text).
/// Kept in one place so GitHub Pages docs can mirror the same ideas in Markdown.
/// When you add a new curriculum theme (new level block), update this string **and** docs/math-concepts.md / exam prep pages as needed.
/// </summary>
public static class LearningArticleLibrary
{
    /// <summary>Large verbatim TMP block; embedded newlines are meaningful for paragraph breaks.</summary>
    public static string GetLevelSelectArticleRichText()
    {
        return @"<b><size=120%>Math concepts — in-game reader</size></b>
<size=90%>Use <b>Math concepts</b> while playing, or <b>Math tips & snippets</b> on Level select — same scrollable notes. Tap the dark backdrop or <b>Close</b> to dismiss.</size>

<b><size=115%>How this game teaches calculus (read the graph while you play)</size></b>
<size=92%>
• <b>Main curve</b> — the thick path on the Cartesian grid is y = f(x) for this stage. Your character tries to stand on platforms that follow that shape. The <b>Equation</b> label (when visible) names the rule being plotted.

• <b>Derivative curve f′(x)</b> — drawn as a second graph; values come from a <i>numerical</i> derivative (sampled slopes). On many levels, <b>where f′ is large enough you get solid ground</b>; where it falls short you can fall through — the platformer is tied to slope logic.

• <b>Stages & “pops”</b> — the <b>Stage</b> HUD counts story beats. As you move right, the derivative visualization can <i>pop</i> or recolor at set x-values (like curtain lifts between ideas in a lesson).

• <b>Riemann sums / area</b> — some levels shade rectangles or build <i>stair</i> platforms from left-, right-, or midpoint-rule samples. That pictures ∫ f(x) dx as the limit of Σ f(x*)·Δx.

• <b>Story text</b> — the banner at the top ties each level to the math (series, polar, physics metaphors, etc.). Pause and read; it fades so the run stays readable.

Everything below is a <b>topic glossary</b> you can skim between deaths or after finishing a graph.
</size>

<b><size=110%>1. Derivatives = slope & rate</size></b>
The derivative f'(x) measures how steeply f(x) rises or falls. In physics it is often a <i>rate</i>: how fast position changes (velocity), how fast temperature changes along a bar, etc. In this game, the derivative helps decide where the ground exists.

<b><size=110%>2. Parabola (power / quadratic)</size></b>
A quadratic y = a(x−h)²+k is the shape of projectile motion in ideal textbook setups and many optimization problems (min/max). One smooth hump; the slope switches sign at the vertex.

<b><size=110%>3. Sine & cosine = waves & rotation</size></b>
Sines and cosines describe vibrations, AC signals, sound, and anything that repeats. Cosine is sine shifted: same wave, different starting phase. Complex numbers (below) make these waves easier to solve in circuits and vibrations.

<b><size=110%>4. Absolute value & kinks</size></b>
|x| bends the graph so there is a corner on the axis. The derivative jumps there in ideal math — in real programs we plot smooth samples, but the idea matters: nonsmooth points need special care in analysis and simulation.

<b><size=110%>5. Taylor & Maclaurin series</size></b>
Smooth functions can be approximated near a point by polynomials with matching derivatives. Maclaurin means “expand around 0.” More terms usually improve the fit nearby; the full infinite sum is the series (where it converges).

<b><size=110%>6. Geometric series</size></b>
Sums u⁰+u¹+u²+… appear in probability, signal processing, and digital math. When |u|<1 the tail shrinks and the infinite sum has a clean closed form; that is the same mood as stability and “things settle.”

<b><size=110%>7. Multivariable slices</size></b>
Surfaces z = f(x,y) can be cut by fixing y=y₀ — you get a 1D curve in x. That is how higher-dimensional calculus is often reasoned about in engineering: fix all but one variable, take partial derivatives, gradients, directional slopes.

<b><size=110%>8. Integrals & area (Riemann sums)</size></b>
The definite integral ∫ₐᵇ f(x) dx is the signed area under the curve. Riemann sums chop [a,b] into thin rectangles: pick sample heights (left, right, midpoint), add f(x*)·Δx. More rectangles → closer to the true integral.

<b><size=110%>9. Engineering math — modeling mindset</size></b>
Engineering math picks tools that match the world: linear algebra for structures and networks, complex numbers / phasors for steady AC, differential equations for motion and heat, transforms (Laplace/Fourier) for signals and control. The goal is a <i>usable model</i>, then check it against reality.

<b><size=110%>10. Damped oscillation</size></b>
Many systems lose energy while oscillating: e^{-decay}·sin(ωt) style decay is the cartoon of that idea — envelope shrinks, oscillation persists briefly. Mechanical damping, resistor–capacitor–inductor circuits, and control systems all share this language.

<b><size=110%>11. Catenary & cosh</size></b>
A hanging cable under its own weight forms a catenary; cosh is the hyperbolic cosine that models that ideal shape (and shows up in hyperbolic PDEs and relativity too). Different from a parabola even if both look “like arches.”

<b><size=110%>12. Rectified sine |sin|</size></b>
Full-wave rectification flips negative lobes upward — a first step in turning AC into something closer to DC for power supplies. Corners at zeros mean derivatives jump — a reminder that idealized circuits still start from calculus intuitions.

<b><size=110%>13. Circle (x−h)² + (y−k)² = R²</size></b>
A circle is usually written implicitly. Solving for y gives two branches (±√). The game’s circle stage uses the <i>upper</i> semicircle so the path stays a function y(x) over one sweep. Implicit differentiation yields dy/dx = −(x−h)/(y−k); at the ends of the diameter the tangent is vertical (slope blows up).

<b><size=120%>──────── Aerospace engineering & aerodynamics ────────</size></b>
<size=92%>Levels prefixed <b>Aerospace:</b> turn textbook flight‑vehicle math into paths you run. They are <i>toy models</i> for pedagogy — not CFD, flight test, or ITAR‑grade simulations.</size>

<b><size=118%>Lift, drag, atmosphere</size></b>
<b>C_L(α)</b> grows ~linearly before <b>stall</b> (flow separation), then drops — slopes & breakpoints drive your platforms. <b>Drag polar</b> C_D = C_{D0} + K C_L² is a parabola in C_L: min‑drag sweet spots matter for glide & endurance. <b>Isothermal atmosphere</b> uses ρ(h) ∝ e^{−h/H} (scale height H): density drives q = ½ρV², Reynolds, thrust lapse.

<b><size=118%>Stability, unsteady aero, entry</size></b>
<b>Phugoid / short‑period</b> moods use damped sinusoids from linearized dynamics (eigenvalues in state space). <b>Strouhal</b> shedding ties frequency to U/D with sine‑like traces. <b>Newtonian sin²α</b> sketches hypersonic windward pressure teaching curves. <b>Re‑entry decay</b> uses an exponential envelope mood for “how fast heating threat relaxes” in simplified stories.

<b><size=90%>See docs/engineering-math.md § Aerospace for a longer map.</size></b>

<b><size=120%>──────── Exam prep (separate tracks) ────────</size></b>

<b><size=118%>TMUA — Test of Mathematics for University Admission (UK)</size></b>
Two-paper <b>multiple choice</b>. Calculus shows up as fast <b>chain/product/quotient</b> fluency, sketch reasoning from <b>f′</b> and <b>f″</b>, ∫ as signed area / FTC mood, <b>domains</b> of ln & √, exp/log inequalities, limits & asymptotes. Typical traps: |x| kinks, endpoint maxima, “exactly one option is true” elimination. <size=90%>No reproduced questions—use official TMUA materials.</size> → <b>docs/tmua-calculus.md</b>

<b><size=118%>MAT — Mathematics Admissions Test (Oxford / UK)</size></b>
Emphasis on <b>multi-step reasoning</b> and exact algebra—not the same pacing as TMUA. Calculus supports <b>graph sense</b>, implicit curves, inequalities, and “how many solutions?” via monotonicity. Format evolves; check Oxford’s official MAT page. <size=90%>No reproduced questions—use official MAT papers.</size> → <b>docs/mat-calculus.md</b>

<b><size=118%>AP Calculus BC (College Board, US)</size></b>
BC stacks <b>series & Taylor</b>, <b>parametric / polar / vector-valued</b> motion, <b>DEs</b> (logistic, separable), and richer integration (incl. improper) beside AB. Polar recap: A = ½∫ r² dθ; arc length √(r²+(dr/dθ)²). In-game: stages <b>BC:</b>, <b>Polar:</b>, <b>Circle</b>, plus Maclaurin & Riemann levels. <size=90%>Pair with CB Course Description & FRQs.</size> → <b>docs/ap-calculus-bc.md</b>

<b><size=118%>AP Physics C (College Board, US)</size></b>
<b>Calculus-first</b> mechanics & E&M: <b>v = dr/dt</b>, <b>a = dv/dt</b>, work integrals, <b>τ = dL/dt</b>, <b>L = Iω</b> on fixed axis, angular momentum conservation when τ_ext = 0; E&M uses flux / line-integral setups. Game motifs: <i>exponential decay (τ)</i>, <i>projectile parabola</i>, <i>rotation / SHM</i>—visual hooks only. → <b>docs/ap-physics-c.md</b>

<b><size=110%>Where to read more</size></b>
<b>docs/math-concepts.md</b> (index), <b>docs/engineering-math.md</b> (applied circuits/oscillations), and the four prep files above on GitHub Pages.

— © 2022-2026 · GAME GENESIS × ORCH AEROSPACE · First Principles
";
    }
}
