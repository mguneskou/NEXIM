// NEXIM — Single-photon Monte Carlo tracer for plane-parallel atmospheres.
//
// Implements the standard backward Monte Carlo algorithm:
//   • Free-path sampling:  l = −ln(ξ) / σ_ext  (exponential distribution)
//   • Scattering:          weight × SSA; new direction from HG phase function
//   • Russian roulette:    applied when weight < 1e-3 (survival prob 0.1)
//   • Surface:             Lambertian reflection (weight × ρ, new cosine-weighted direction)
//   • TOA escape:          upward photons contribute to upwelling radiance
//
// References:
//   Mayer (2009) EPJ Web of Conferences 1:75 — review of MC methods in RT
//   Marshak & Davis (2005) "3D Radiative Transfer in Cloudy Atmospheres" — DDA
//   Chandrasekhar (1960) "Radiative Transfer", §17 — HG rotation formula

namespace NEXIM.Core.Atmospheric.MonteCarlo;

/// <summary>
/// Stateless photon tracer. Call <see cref="Trace"/> for each photon.
/// All random numbers come from the caller-supplied <see cref="Random"/> instance,
/// allowing reproducible per-thread seeding in <see cref="MonteCarloSolver"/>.
/// </summary>
public static class PhotonTracer
{
    private const double WeightThreshold    = 1e-3;
    private const double SurvivalProbability = 0.1;
    private const int    MaxScatters        = 500;

    /// <summary>
    /// Trace a single photon through the atmospheric volume.
    /// </summary>
    /// <param name="photon">Initial photon state (modified in-place).</param>
    /// <param name="volume">Pre-built atmospheric voxel grid.</param>
    /// <param name="surfaceReflectance">Lambertian surface reflectance in [0, 1].</param>
    /// <param name="rng">Random number generator (thread-local).</param>
    /// <returns>
    /// Statistical weight contribution to the upwelling radiance at TOA.
    /// Zero if the photon was absorbed or escaped downward.
    /// </returns>
    public static double Trace(
        ref PhotonPacket photon,
        AtmosphericVolume volume,
        double surfaceReflectance,
        Random rng)
    {
        while (photon.IsAlive)
        {
            // ── Sample free path in optical depth ─────────────────────────
            double tau = -Math.Log(rng.NextDouble() + 1e-300);

            // Traverse voxels until optical depth τ is consumed
            double remaining = tau;
            bool   consumed  = false;

            // ── DDA voxel traversal (Marshak & Davis 2005, Ch. 2) ─────────
            // We move the photon through the grid one voxel crossing at a time.
            // For a 1D atmosphere (NX=NY=1), only Z-crossings matter.

            for (int step = 0; step < 10_000; step++)
            {
                var (ext, ssaV, gV) = volume.GetVoxel(photon.X, photon.Y, photon.Z);
                double sigma = Math.Max(ext, 1e-30);   // km⁻¹

                // How far can the photon travel within the current voxel?
                double dz = volume.GridSpacing_km;
                double pathToVoxelBoundary = NextVoxelCrossing(photon, dz);

                // Optical depth available in this voxel
                double tauAvailable = sigma * pathToVoxelBoundary;

                if (remaining <= tauAvailable)
                {
                    // Interaction occurs inside this voxel
                    double dl = remaining / sigma;
                    photon.X += dl * photon.U;
                    photon.Y += dl * photon.V;
                    photon.Z += dl * photon.W;
                    consumed = true;
                    break;
                }

                // Cross the voxel boundary
                remaining -= tauAvailable;
                photon.X += pathToVoxelBoundary * photon.U;
                photon.Y += pathToVoxelBoundary * photon.V;
                photon.Z += pathToVoxelBoundary * photon.W;

                // ── Boundary conditions ───────────────────────────────────
                if (photon.Z >= volume.ToaAltitude_km)
                {
                    // Escaped at TOA — upward photons counted
                    if (photon.W > 0.0)
                    {
                        double contrib = photon.Weight;
                        photon.Status = PhotonStatus.Escaped;
                        return contrib;
                    }
                    // Downward at TOA: re-enter (shouldn't happen with solar illumination)
                    photon.Z = volume.ToaAltitude_km - 1e-6;
                    photon.W = -photon.W;
                    continue;
                }

                if (photon.Z <= 0.0)
                {
                    // Hit surface
                    photon.Z = 1e-9;
                    photon.Weight *= surfaceReflectance;

                    if (photon.Weight < 1e-30)
                    {
                        photon.Status = PhotonStatus.Absorbed;
                        return 0.0;
                    }

                    // Lambertian reflection: cosine-weighted upward hemisphere
                    double cosTheta = Math.Sqrt(rng.NextDouble());
                    double sinTheta = Math.Sqrt(Math.Max(0.0, 1.0 - cosTheta * cosTheta));
                    double phi      = 2.0 * Math.PI * rng.NextDouble();
                    photon.U = sinTheta * Math.Cos(phi);
                    photon.V = sinTheta * Math.Sin(phi);
                    photon.W = cosTheta;   // always upward
                    photon.ScatterCount++;
                    remaining = -Math.Log(rng.NextDouble() + 1e-300); // new free path
                    consumed  = false;
                    continue;
                }
            }

            if (!consumed)
            {
                // Runaway traversal — absorb to avoid infinite loop
                photon.Status = PhotonStatus.Absorbed;
                return 0.0;
            }

            // ── Scattering event ──────────────────────────────────────────
            var (extI, ssaI, gI) = volume.GetVoxel(photon.X, photon.Y, photon.Z);
            photon.Weight *= ssaI;

            // Russian roulette
            if (photon.Weight < WeightThreshold)
            {
                if (rng.NextDouble() > SurvivalProbability)
                {
                    photon.Status = PhotonStatus.Absorbed;
                    return 0.0;
                }
                photon.Weight /= SurvivalProbability;
            }

            // Sample new direction from Henyey-Greenstein phase function
            (photon.U, photon.V, photon.W) = SampleHG(gI, photon.U, photon.V, photon.W, rng);

            photon.ScatterCount++;
            if (photon.ScatterCount >= MaxScatters)
            {
                photon.Status = PhotonStatus.Absorbed;
                return 0.0;
            }
        }

        return 0.0;
    }

    // ── Phase function sampling ───────────────────────────────────────────────

    /// <summary>
    /// Sample a new direction from the Henyey-Greenstein phase function.
    /// Analytical formula from Henyey &amp; Greenstein (1941) Astrophys. J. 93:70.
    /// </summary>
    private static (double u, double v, double w) SampleHG(
        double g, double u0, double v0, double w0, Random rng)
    {
        double cosTheta;
        if (Math.Abs(g) < 1e-6)
        {
            // Isotropic scattering
            cosTheta = 2.0 * rng.NextDouble() - 1.0;
        }
        else
        {
            double xi  = rng.NextDouble();
            double tmp = (1.0 - g * g) / (1.0 - g + 2.0 * g * xi);
            cosTheta   = (1.0 + g * g - tmp * tmp) / (2.0 * g);
            cosTheta   = Math.Clamp(cosTheta, -1.0, 1.0);
        }

        double sinTheta = Math.Sqrt(Math.Max(0.0, 1.0 - cosTheta * cosTheta));
        double phi      = 2.0 * Math.PI * rng.NextDouble();
        double cosPhi   = Math.Cos(phi);
        double sinPhi   = Math.Sin(phi);

        // Rotate scattering angle into global frame (Chandrasekar 1960 §17)
        if (Math.Abs(w0) > 0.9999)
        {
            // Near-vertical photon: degenerate case
            double s = (w0 >= 0.0) ? 1.0 : -1.0;
            return (sinTheta * cosPhi, sinTheta * sinPhi, s * cosTheta);
        }

        double sinInc = Math.Sqrt(1.0 - w0 * w0);  // sin of incident polar angle
        double factor = sinTheta / sinInc;

        double uNew = u0 * cosTheta + factor * (u0 * w0 * cosPhi - v0 * sinPhi);
        double vNew = v0 * cosTheta + factor * (v0 * w0 * cosPhi + u0 * sinPhi);
        double wNew = w0 * cosTheta - factor * sinInc * sinInc * cosPhi;

        // Re-normalise to handle accumulated floating-point error
        double norm = Math.Sqrt(uNew * uNew + vNew * vNew + wNew * wNew);
        if (norm > 1e-10)
        {
            uNew /= norm; vNew /= norm; wNew /= norm;
        }
        return (uNew, vNew, wNew);
    }

    // ── Voxel crossing ────────────────────────────────────────────────────────

    /// <summary>
    /// Compute the physical path length (km) to the next voxel face crossing
    /// in any dimension, limited to <paramref name="dz"/> (maximum one voxel).
    /// Uses a large value when the direction component is near zero.
    /// </summary>
    private static double NextVoxelCrossing(in PhotonPacket p, double dz)
    {
        const double BigDist = 1e20;

        // Z crossing
        double tZ;
        if (p.W > 1e-15)
        {
            double zTop = (Math.Floor(p.Z / dz) + 1.0) * dz;
            tZ = (zTop - p.Z) / p.W;
        }
        else if (p.W < -1e-15)
        {
            double zBot = Math.Floor(p.Z / dz) * dz;
            tZ = (zBot - p.Z) / p.W;  // W negative → positive result
        }
        else
        {
            tZ = BigDist;
        }

        // X and Y crossings (only matter when NX > 1 or NY > 1)
        double tX = BigDist, tY = BigDist;
        if (Math.Abs(p.U) > 1e-15)
        {
            double xFace = (p.U > 0)
                ? (Math.Floor(p.X / dz) + 1.0) * dz
                : Math.Floor(p.X / dz) * dz;
            tX = Math.Abs((xFace - p.X) / p.U);
        }
        if (Math.Abs(p.V) > 1e-15)
        {
            double yFace = (p.V > 0)
                ? (Math.Floor(p.Y / dz) + 1.0) * dz
                : Math.Floor(p.Y / dz) * dz;
            tY = Math.Abs((yFace - p.Y) / p.V);
        }

        double t = Math.Min(tZ, Math.Min(tX, tY));
        // Clamp to at most one voxel width plus a small epsilon to ensure crossing
        return Math.Min(t + 1e-9, dz + 1e-9);
    }
}
