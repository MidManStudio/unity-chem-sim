// crates/chemistry_core/src/spatial_hash.rs
//! Uniform-grid spatial hash for O(1)-average neighbor queries.
//!
//! Cell size == cutoff radius, so any pair within `cutoff` of each other is
//! guaranteed to land in the same cell or one of its 26 neighbors — only
//! 27 cells ever need checking per atom, never the full atom list.
//!
//! Uses `FxHasher` (see `fx_hash.rs`) instead of std's default SipHash —
//! cell keys are `(i32,i32,i32)` derived from atom positions this sim
//! controls, not attacker-supplied data, so SipHash's HashDoS resistance
//! is overhead with nothing to defend against here.
//!
//! Correctness-first pass otherwise: cells are a `HashMap<CellKey, Vec<u32>>`,
//! cleared and refilled each `rebuild()` (not reallocated — bucket `Vec`
//! capacity is kept across frames, only their contents are cleared). Flat
//! sorted-bucket array is the next thing to reach for if the bench numbers
//! justify it — see `.github/workflows/rust-bench.yml`.

use std::collections::HashMap;
use mid_math::Vec3;
use crate::fx_hash::FxBuildHasher;

pub type CellKey = (i32, i32, i32);

pub struct SpatialHash {
    cell_size: f32,
    cells: HashMap<CellKey, Vec<u32>, FxBuildHasher>,
}

impl SpatialHash {
    pub fn new(cell_size: f32) -> Self {
        Self { cell_size: cell_size.max(1e-4), cells: HashMap::default() }
    }

    /// Current cell size (== the cutoff this grid was built for). Callers
    /// that own a persistent grid (`SimContext`) use this to detect when
    /// `cutoff` has changed and the grid needs rebuilding from scratch.
    #[inline]
    pub fn cell_size(&self) -> f32 {
        self.cell_size
    }

    #[inline]
    fn cell_of(&self, p: Vec3) -> CellKey {
        (
            (p.x / self.cell_size).floor() as i32,
            (p.y / self.cell_size).floor() as i32,
            (p.z / self.cell_size).floor() as i32,
        )
    }

    /// Rebuild the grid from current atom positions. Existing bucket `Vec`s
    /// are cleared and reused rather than dropped, so cells that are
    /// repeatedly occupied across frames don't pay repeated reallocation.
    pub fn rebuild(&mut self, positions: &[Vec3]) {
        for bucket in self.cells.values_mut() {
            bucket.clear();
        }
        for (i, &p) in positions.iter().enumerate() {
            let key = self.cell_of(p);
            self.cells.entry(key).or_insert_with(Vec::new).push(i as u32);
        }
    }

    /// Visit every atom index sharing a cell (or an adjacent cell) with `p`,
    /// via the 3x3x3 neighborhood. `f` runs once per candidate index —
    /// this only narrows the candidate set, callers still need their own
    /// distance check (candidates include everything in a 3x3x3 cell block,
    /// not just things strictly within `cutoff`).
    pub fn for_each_candidate(&self, p: Vec3, mut f: impl FnMut(u32)) {
        let (cx, cy, cz) = self.cell_of(p);
        for dx in -1..=1 {
            for dy in -1..=1 {
                for dz in -1..=1 {
                    if let Some(bucket) = self.cells.get(&(cx + dx, cy + dy, cz + dz)) {
                        for &idx in bucket {
                            f(idx);
                        }
                    }
                }
            }
        }
    }
}
