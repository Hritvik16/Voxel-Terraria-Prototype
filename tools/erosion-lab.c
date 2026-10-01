// tools/erosion-lab.c -- a fast bench for tuning TerrainGen's erosion
// (Assets/TerrainGen/Erosion.cs). SAME ALGORITHM, line for line: uplift toward
// the target, a priority flood every FLOOD_EVERY iterations, steepest-descent
// receivers on the water surface, multiple-flow area, implicit stream power,
// talus. Reads the inputs WorldMap.BuildV3 dumps (WorldMap.DumpErosionInputsTo)
// and writes the eroded heights, so a parameter can be tried in seconds instead
// of a ten-minute Unity run. Numbers found here go back into Erosion.cs /
// Regions.cs and are re-verified by the real EditMode tests.
//
//   erosion-lab in.bin out.bin iterations uplift escale talus_scale flood_every [hard_factor]
//   clang -O3 -o tools/erosion-lab tools/erosion-lab.c -lm
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static const int DX[8] = { -1, 0, 1, -1, 1, -1, 0, 1 };
static const int DZ[8] = { -1, -1, -1, 0, 0, 1, 1, 1 };
#define DIAG 1.41421356f
#define ISDIAG(k) ((k) == 0 || (k) == 2 || (k) == 5 || (k) == 7)

static float *hk; static int *hv; static int hn;
static int less(int a, int b) { return hk[a] < hk[b] || (hk[a] == hk[b] && hv[a] < hv[b]); }
static void swp(int a, int b) { float k = hk[a]; hk[a] = hk[b]; hk[b] = k; int v = hv[a]; hv[a] = hv[b]; hv[b] = v; }
static void push(float k, int v) { int i = ++hn; hk[i] = k; hv[i] = v; while (i > 1 && less(i, i >> 1)) { swp(i, i >> 1); i >>= 1; } }
static void pop(float *k, int *v) {
    *k = hk[1]; *v = hv[1]; hk[1] = hk[hn]; hv[1] = hv[hn]; hn--;
    int i = 1;
    for (;;) { int l = i << 1, r = l + 1, m = i; if (l <= hn && less(l, m)) m = l; if (r <= hn && less(r, m)) m = r; if (m == i) break; swp(i, m); i = m; }
}

static int hard(float h) { float f = h / 600.f - floorf(h / 600.f); return f > 0.60f; }  // Regions.HardLayer

int main(int argc, char **argv) {
    if (argc < 8) { fprintf(stderr, "usage: see header\n"); return 2; }
    FILE *f = fopen(argv[1], "rb"); int n; fread(&n, 4, 1, f);
    int N = n * n;
    float *target = malloc(N * 4), *erod = malloc(N * 4), *talus = malloc(N * 4); unsigned char *lay = malloc(N);
    fread(target, 4, N, f); fread(erod, 4, N, f); fread(talus, 4, N, f); fread(lay, 1, N, f); fclose(f);
    int iterations = atoi(argv[3]); float uplift = atof(argv[4]), escale = atof(argv[5]), tscale = atof(argv[6]);
    int floodEvery = atoi(argv[7]); float hardF = argc > 8 ? atof(argv[8]) : 0.08f;
    float cellVoxels = 160.f;
    float *h = malloc(N * 4), *area = malloc(N * 4), *fill = malloc(N * 4);
    int *order = malloc(N * 4), *parent = malloc(N * 4), *recv = malloc(N * 4); unsigned char *vis = malloc(N);
    hk = malloc((N + 1) * 4); hv = malloc((N + 1) * 4);
    for (int i = 0; i < N; i++) h[i] = target[i] <= 0 ? target[i] : target[i] * 0.2f;
    int count = 0;
    for (int it = 0; it < iterations; it++) {
        for (int i = 0; i < N; i++) if (target[i] > 0 && h[i] < target[i]) h[i] += uplift * (target[i] - h[i]);
        if (it % floodEvery == 0) {
            count = 0; hn = 0;
            for (int i = 0; i < N; i++) { vis[i] = 0; parent[i] = i; }
            for (int z = 0; z < n; z++) for (int x = 0; x < n; x++) {
                int i = z * n + x, edge = x == 0 || z == 0 || x == n - 1 || z == n - 1;
                if (h[i] <= 0 || edge) { vis[i] = 1; fill[i] = h[i]; push(h[i], i); }
            }
            while (hn > 0) {
                float fh; int i; pop(&fh, &i); order[count++] = i;
                int x = i % n, z = i / n;
                for (int k = 0; k < 8; k++) {
                    int xx = x + DX[k], zz = z + DZ[k];
                    if (xx < 0 || zz < 0 || xx >= n || zz >= n) continue;
                    int j = zz * n + xx; if (vis[j]) continue;
                    vis[j] = 1; parent[j] = i; fill[j] = fmaxf(h[j], fh + 1e-3f); push(fill[j], j);
                }
            }
        }
        for (int i = 0; i < N; i++) fill[i] = fmaxf(fill[i], h[i]);
        for (int z = 0; z < n; z++) for (int x = 0; x < n; x++) {
            int i = z * n + x; recv[i] = parent[i];
            if (h[i] <= 0) { recv[i] = i; continue; }
            float best = 0;
            for (int k = 0; k < 8; k++) {
                int xx = x + DX[k], zz = z + DZ[k]; if (xx < 0 || zz < 0 || xx >= n || zz >= n) continue;
                int j = zz * n + xx; float s = (fill[i] - fill[j]) * (ISDIAG(k) ? 0.70710678f : 1.f);
                if (s > best) { best = s; recv[i] = j; }
            }
        }
        for (int i = 0; i < N; i++) area[i] = 1;
        for (int c = count - 1; c >= 0; c--) {
            int i = order[c]; if (h[i] <= 0) continue;
            int x = i % n, z = i / n; float sum = 0;
            for (int k = 0; k < 8; k++) {
                int xx = x + DX[k], zz = z + DZ[k]; if (xx < 0 || zz < 0 || xx >= n || zz >= n) continue;
                float s = fill[i] - fill[zz * n + xx]; if (s > 0) sum += s * (ISDIAG(k) ? 0.70710678f : 1.f);
            }
            if (sum <= 0) { if (parent[i] != i) area[parent[i]] += area[i]; continue; }
            for (int k = 0; k < 8; k++) {
                int xx = x + DX[k], zz = z + DZ[k]; if (xx < 0 || zz < 0 || xx >= n || zz >= n) continue;
                int j = zz * n + xx; float s = fill[i] - fill[j];
                if (s > 0) area[j] += area[i] * s * (ISDIAG(k) ? 0.70710678f : 1.f) / sum;
            }
        }
        for (int c = 0; c < count; c++) {
            int i = order[c], r = recv[i];
            if (r == i || h[i] <= 0 || h[i] <= h[r]) continue;
            int dxr = abs(i % n - r % n), dzr = abs(i / n - r / n);
            float d = (dxr + dzr == 2) ? DIAG : 1.f;
            float e = erod[i] * escale; if (lay[i] && hard(h[i])) e *= hardF;
            float F = e * sqrtf(area[i]) / d;
            h[i] = (h[i] + F * h[r]) / (1.f + F);
        }
        for (int z = 1; z < n - 1; z++) for (int x = 1; x < n - 1; x++) {
            int i = z * n + x; if (h[i] <= 0) continue;
            float tal = talus[i] * tscale * cellVoxels; if (lay[i] && hard(h[i])) tal *= 3.f;
            for (int k = 0; k < 8; k++) {
                int j = (z + DZ[k]) * n + x + DX[k];
                float lim = ISDIAG(k) ? tal * DIAG : tal, diff = h[i] - h[j];
                if (diff > lim) { float mv = (diff - lim) * 0.25f; h[i] -= mv; h[j] += mv; }
            }
        }
    }
    FILE *o = fopen(argv[2], "wb"); fwrite(&n, 4, 1, o); fwrite(h, 4, N, o); fclose(o);
    return 0;
}
