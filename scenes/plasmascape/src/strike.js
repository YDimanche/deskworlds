// The arc the globe throws outside its glass. The host names a target in the room
// (a double-clicked desktop icon) and the strike leaps from the glass to it, forks
// on the way and detonates where it lands. Its segments ride the plasma's ribbon
// renderer, so the bolt glows, blooms and reflects in the table for free.
import { GLOBE, SEGMENT_FLOATS } from './plasma.js';

export const MAX_STRIKE_SEGMENTS = 800;
export const ARC_LIFE = 0.42;        // how long the bolt keeps striking, seconds
export const BURST_LIFE = 0.78;      // how long the detonation at the target lingers
const BURST_DELAY = 0.05;            // the flash lands just after the bolt appears
const MIN_RADIUS = 1.9;              // a target inside this radius is pushed out of the globe
const MAX_BOLTS = 3;
const MAX_POINTS = 56;
const FORKS = 2;
const SPOKES = 10;

const clamp = (x, a, b) => Math.min(b, Math.max(a, x));

// Smooth value noise in [-1, 1], the same scheme as the plasma's.
function hash(x, y, z) {
  let h = Math.imul(x, 374761393) ^ Math.imul(y, 668265263) ^ Math.imul(z, 1274126177);
  h = Math.imul(h ^ (h >>> 13), 1103515245);
  return ((h ^ (h >>> 16)) >>> 0) / 2147483648 - 1;
}
function noise(x, y, z) {
  const ix = Math.floor(x), iy = Math.floor(y), iz = Math.floor(z);
  const fx = x - ix, fy = y - iy, fz = z - iz;
  const u = fx * fx * fx * (fx * (fx * 6 - 15) + 10), v = fy * fy * fy * (fy * (fy * 6 - 15) + 10), w = fz * fz * fz * (fz * (fz * 6 - 15) + 10);
  const l = (a, b, t) => a + (b - a) * t;
  return l(
    l(l(hash(ix, iy, iz), hash(ix + 1, iy, iz), u), l(hash(ix, iy + 1, iz), hash(ix + 1, iy + 1, iz), u), v),
    l(l(hash(ix, iy, iz + 1), hash(ix + 1, iy, iz + 1), u), l(hash(ix, iy + 1, iz + 1), hash(ix + 1, iy + 1, iz + 1), u), v), w);
}

export function createStrike() {
  const segments = new Float32Array(MAX_STRIKE_SEGMENTS * SEGMENT_FLOATS);
  const state = { time: 0, segmentCount: 0 };
  const bolts = [];
  const path = new Float32Array(MAX_POINTS * 3);
  const forkPts = new Float32Array(FORKS * 3);
  let count = 0;

  function emit(ax, ay, az, aw, bx, by, bz, bw, i0, i1, s0, s1) {
    if (count >= MAX_STRIKE_SEGMENTS) return;
    const o = count * SEGMENT_FLOATS;
    segments[o] = ax; segments[o + 1] = ay; segments[o + 2] = az; segments[o + 3] = aw;
    segments[o + 4] = bx; segments[o + 5] = by; segments[o + 6] = bz; segments[o + 7] = bw;
    segments[o + 8] = i0; segments[o + 9] = i1; segments[o + 10] = s0; segments[o + 11] = s1;
    count++;
  }

  function step(dt) {
    state.time += dt;
    while (bolts.length && state.time - bolts[0].born > BURST_DELAY + BURST_LIFE) bolts.shift();
  }

  function strikeAt(target) {
    let { x, y, z } = target;
    let length = Math.hypot(x, y, z);
    if (!Number.isFinite(length) || length < 1e-3) return;
    if (length < MIN_RADIUS) {
      const k = MIN_RADIUS / length;
      x *= k; y *= k; z *= k;
      length = MIN_RADIUS;
    }
    const glass = GLOBE.outer * 1.02 / length;
    bolts.push({
      sx: x * glass, sy: y * glass, sz: z * glass,
      tx: x, ty: y, tz: z,
      born: state.time, seed: Math.random() * 512,
    });
    if (bolts.length > MAX_BOLTS) bolts.shift();
  }

  // One jagged glowing stroke between two points. The path kinks along two axes
  // perpendicular to the stroke and re-forms about twenty times a second, with a
  // finer shimmer in between, like a live arc.
  function stroke(sx, sy, sz, tx, ty, tz, seed, intensity, width, s0, s1) {
    const ax = tx - sx, ay = ty - sy, az = tz - sz;
    const length = Math.hypot(ax, ay, az);
    if (length < 1e-4) return 0;
    const n = clamp(Math.round(length / 0.05) + 1, 4, MAX_POINTS);
    const dx = ax / length, dy = ay / length, dz = az / length;
    // Two jitter axes perpendicular to the stroke.
    let ux, uy, uz;
    if (Math.abs(dy) < 0.9) { ux = -dz; uy = 0; uz = dx; }
    else { ux = 0; uy = dz; uz = -dy; }
    const ul = Math.hypot(ux, uy, uz) || 1;
    ux /= ul; uy /= ul; uz /= ul;
    let vx = dy * uz - dz * uy, vy = dz * ux - dx * uz, vz = dx * uy - dy * ux;
    const vl = Math.hypot(vx, vy, vz) || 1;
    vx /= vl; vy /= vl; vz /= vl;

    const jag = length * 0.085 + 0.015;
    const freq = 3.2 + length * 0.55;
    const epoch = Math.floor(state.time * 20) / 20;
    for (let k = 0; k < n; k++) {
      const u = k / (n - 1);
      const shape = Math.pow(Math.sin(Math.PI * u), 0.7);
      const j1 = noise(u * freq + seed, epoch * 13.7, 1) + 0.5 * noise(u * freq * 3.1 + seed, state.time * 47, 2);
      const j2 = noise(u * freq + seed + 31.7, epoch * 13.7, 3) + 0.5 * noise(u * freq * 3.1 + seed + 5.5, state.time * 47, 4);
      const o = k * 3;
      path[o] = sx + ax * u + (ux * j1 + vx * j2) * jag * shape;
      path[o + 1] = sy + ay * u + (uy * j1 + vy * j2) * jag * shape;
      path[o + 2] = sz + az * u + (uz * j1 + vz * j2) * jag * shape;
    }
    for (let k = 0; k < n - 1; k++) {
      const u0 = k / (n - 1), u1 = (k + 1) / (n - 1), a = k * 3, b = a + 3;
      const bead0 = 0.8 + 0.4 * noise(k * 0.9 + seed, state.time * 33, 5);
      const bead1 = 0.8 + 0.4 * noise(k * 0.9 + seed + 1, state.time * 33, 5);
      emit(path[a], path[a + 1], path[a + 2], width * (1.3 - 0.5 * u0),
        path[b], path[b + 1], path[b + 2], width * (1.3 - 0.5 * u1),
        intensity * (1.04 - 0.4 * u0) * bead0, intensity * (1.04 - 0.4 * u1) * bead1,
        s0 + (s1 - s0) * u0, s0 + (s1 - s0) * u1);
    }
    return n;
  }

  function build() {
    count = 0;
    for (const bolt of bolts) {
      const age = state.time - bolt.born;
      if (age < 0) continue;
      // The bolt from the glass to the target, forking on the way.
      if (age <= ARC_LIFE) {
        const t = age / ARC_LIFE;
        const flicker = 0.45 + 0.55 * (0.5 + 0.5 * noise(bolt.seed, state.time * 34, 6));
        const glow = Math.pow(1 - t, 0.65) * flicker;
        if (glow > 0.02) {
          const n = stroke(bolt.sx, bolt.sy, bolt.sz, bolt.tx, bolt.ty, bolt.tz, bolt.seed, 3.4 * glow, 0.012, 0.12, 0.8);
          if (n >= 4) {
            for (let f = 0; f < FORKS; f++) {
              const k = Math.min(n - 2, Math.round((0.3 + 0.28 * f) * (n - 1))) * 3;
              forkPts[f * 3] = path[k]; forkPts[f * 3 + 1] = path[k + 1]; forkPts[f * 3 + 2] = path[k + 2];
            }
            for (let f = 0; f < FORKS; f++) {
              stroke(forkPts[f * 3], forkPts[f * 3 + 1], forkPts[f * 3 + 2],
                bolt.tx + 0.3 * noise(bolt.seed + f * 13.1, 3.7, 7 + f),
                bolt.ty + 0.2 * noise(bolt.seed + f * 13.1, 9.2, 9 + f),
                bolt.tz + 0.3 * noise(bolt.seed + f * 13.1, 15.8, 11 + f),
                bolt.seed + 41 + f * 7, 1.7 * glow * (0.8 - 0.2 * f), 0.0085, 0.5, 0.85);
            }
          }
        }
      }
      // The detonation: a blinding core and spokes of debris flung outward.
      const burst = age - BURST_DELAY;
      if (burst < 0 || burst > BURST_LIFE) continue;
      const t = burst / BURST_LIFE;
      const fade = Math.pow(1 - t, 1.6);
      const flicker = 0.7 + 0.3 * (0.5 + 0.5 * noise(bolt.seed + 3.3, state.time * 60, 12));
      const core = 9 * fade * fade * flicker;
      if (core > 0.05) {
        emit(bolt.tx - 0.02, bolt.ty - 0.02, bolt.tz - 0.02, 0.055,
          bolt.tx + 0.02, bolt.ty + 0.015, bolt.tz + 0.015, 0.055, core, core * 0.9, 0.55, 0.55);
        emit(bolt.tx, bolt.ty - 0.03, bolt.tz, 0.04,
          bolt.tx + 0.005, bolt.ty + 0.03, bolt.tz + 0.02, 0.04, core * 0.8, core * 0.7, 0.6, 0.6);
      }
      const ease = 1 - Math.pow(1 - t, 2.4);
      for (let j = 0; j < SPOKES; j++) {
        const spoke = bolt.seed + j * 3.7;
        const angle = (noise(spoke, 1.1, 17) * 0.5 + 0.5) * Math.PI * 2;
        const lift = noise(spoke, 2.9, 19);
        const ring = Math.sqrt(Math.max(0, 1 - lift * lift));
        const dx = ring * Math.cos(angle), dy = lift, dz = ring * Math.sin(angle);
        const length = (0.12 + 0.75 * ease) * (0.55 + 0.5 * (noise(spoke, 4.4, 23) * 0.5 + 0.5));
        const power = 5 * fade * (0.55 + 0.45 * (0.5 + 0.5 * noise(spoke + 1, state.time * 50, 29)));
        if (power < 0.05) continue;
        const piece = length / 3;
        let px = bolt.tx, py = bolt.ty, pz = bolt.tz;
        for (let q = 0; q < 3; q++) {
          const u = q / 3;
          const qx = px + (dx + 0.09 * noise(spoke + q * 7.7, 0.5, 31)) * piece;
          const qy = py + (dy + 0.09 * noise(spoke + q * 7.7, 4.2, 33)) * piece;
          const qz = pz + (dz + 0.09 * noise(spoke + q * 7.7, 8.9, 35)) * piece;
          emit(px, py, pz, 0.008 * (1 - 0.5 * u), qx, qy, qz, 0.008 * (1 - 0.5 * (u + 1 / 3)),
            power * (1 - u), power * (1 - u - 1 / 3), 0.55, 0.75);
          px = qx; py = qy; pz = qz;
        }
      }
    }
    state.segmentCount = count;
  }

  return { state, segments, strikeAt, step, build };
}
