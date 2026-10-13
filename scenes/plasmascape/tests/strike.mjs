import assert from 'node:assert/strict';
import { createStrike, MAX_STRIKE_SEGMENTS } from '../src/strike.js';
import { FIXED_STEP, SEGMENT_FLOATS, GLOBE } from '../src/plasma.js';

const run = (strike, seconds) => { for (let i = 0; i < Math.round(seconds / FIXED_STEP); i++) { strike.step(FIXED_STEP); strike.build(); } strike.build(); };

// A strike leaps from the glass to the target: real, finite, bounded geometry.
{
  const strike = createStrike();
  strike.strikeAt({ x: 2, y: 1, z: 0 });
  strike.step(FIXED_STEP);
  strike.build();
  const { segmentCount } = strike.state;
  assert.ok(segmentCount > 40, `A strike draws real geometry (${segmentCount})`);
  assert.ok(segmentCount < MAX_STRIKE_SEGMENTS, 'and never overflows its buffer');
  const first = strike.segments.subarray(0, SEGMENT_FLOATS);
  const r = Math.hypot(2, 1), glass = GLOBE.outer * 1.02;
  assert.ok(Math.hypot(first[0] - 2 / r * glass, first[1] - 1 / r * glass, first[2]) < 1e-6,
    "The bolt is anchored on the glass in the target's direction");
  for (let k = 0; k < segmentCount; k++) {
    const s = strike.segments.subarray(k * SEGMENT_FLOATS, (k + 1) * SEGMENT_FLOATS);
    assert.ok([...s].every(Number.isFinite), 'Segment values are finite');
    assert.ok(s[8] >= 0 && s[9] >= 0 && s[10] >= 0 && s[10] <= 1.0001 && s[11] >= 0 && s[11] <= 1.0001,
      'Intensity is non-negative and colour position in range');
  }
}

// The arc crackles: it changes shape while it lives and is gone soon after.
{
  const strike = createStrike();
  strike.strikeAt({ x: -2, y: 0.5, z: 1 });
  strike.step(FIXED_STEP);
  strike.build();
  const before = Array.from(strike.segments.subarray(0, strike.state.segmentCount * SEGMENT_FLOATS));
  run(strike, 0.2);
  const after = Array.from(strike.segments.subarray(0, strike.state.segmentCount * SEGMENT_FLOATS));
  assert.notDeepEqual(before, after, 'The bolt re-forms as it crackles');
  assert.ok(strike.state.segmentCount > 0, 'and is still striking');
  run(strike, 1.2);
  assert.equal(strike.state.segmentCount, 0, 'The strike dies out');
}

// A target at the centre is ignored, a near one is pushed out of the globe, and
// rapid strikes stay within the buffer.
{
  const strike = createStrike();
  strike.strikeAt({ x: 0, y: 0, z: 0 });
  strike.step(FIXED_STEP);
  strike.build();
  assert.equal(strike.state.segmentCount, 0, 'A target at the centre is ignored');
  strike.strikeAt({ x: 0, y: 0, z: 0.5 });
  strike.step(FIXED_STEP);
  strike.build();
  assert.ok(strike.state.segmentCount > 0, 'A near target is pushed out of the globe');
  for (let i = 0; i < 6; i++) strike.strikeAt({ x: 2 + i, y: 1, z: -1 });
  strike.step(FIXED_STEP);
  strike.build();
  assert.ok(strike.state.segmentCount > 0 && strike.state.segmentCount < MAX_STRIKE_SEGMENTS,
    'Rapid strikes stay within the buffer');
}
console.log('strike ok');
