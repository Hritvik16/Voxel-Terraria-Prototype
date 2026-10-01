// tools/vxrender.c -- geometry pass of the palette renderer (tools/palette-render.py).
//
// Ray-traces a GenMat volume exported by PaletteSceneExport (.vxv) with an exact
// voxel DDA and writes, per pixel, WHAT was hit and HOW it is lit -- never a
// colour. The Python side colours the same buffer with every palette, so every
// palette is judged on byte-identical geometry and light.
//
//   vxrender in.vxv out.gbuf W H  cx cy cz  tx ty tz  fovDeg  sx sy sz  cut cutA cutB
//     camera / target in metres from the volume's (0,0,0) corner; sun = unit
//     vector TOWARD the sun; cut 0 none, 1 remove x<cutA && z<cutB (sample
//     indices), 2 remove y>=cutA.
//
// Water (GenMat 1) is transparent to primary rays (depth + floor recorded) and
// to shadow rays. Everything else non-air is opaque. Build:
//   clang -O3 -o tools/vxrender tools/vxrender.c -lpthread -lm
#include <math.h>
#include <pthread.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

typedef struct { uint8_t mat, floorMat, face, ao; float sun, t, waterDepth; uint32_t hash; } Px;

static int NX, NY, NZ, STEPXZ, STEPY;
static uint8_t *V;
static float SX, SY, SZ;           // metres per sample
static int W, H, CUT, CUTA, CUTB;
static float CAM[3], FWD[3], RIGHT[3], UP[3], SUN[3], TANH;
static Px *OUT;

static inline uint8_t at(int x, int y, int z) {
    if (x < 0 || y < 0 || z < 0 || x >= NX || y >= NY || z >= NZ) return 0;
    if (CUT == 1 && x < CUTA && z < CUTB) return 0;
    if (CUT == 2 && y >= CUTA) return 0;
    return V[((size_t)y * NZ + z) * NX + x];
}

static inline int solid(uint8_t m) { return m != 0 && m != 1; }

/* Slab entry into the box [0,N) in index space. Returns 0 if missed. */
static int enter(const float o[3], const float d[3], float *t0, float *t1, int *axis0) {
    float lo = 0.f, hi = 1e30f;
    *axis0 = 0;
    const float n[3] = { (float)NX, (float)NY, (float)NZ };
    for (int a = 0; a < 3; a++) {
        if (fabsf(d[a]) < 1e-12f) { if (o[a] < 0 || o[a] >= n[a]) return 0; continue; }
        float ta = (0 - o[a]) / d[a], tb = (n[a] - o[a]) / d[a];
        if (ta > tb) { float s = ta; ta = tb; tb = s; }
        if (ta > lo) { lo = ta; *axis0 = a; }   // the plane the ray ENTERS through
        if (tb < hi) hi = tb;
    }
    *t0 = lo; *t1 = hi;
    return lo <= hi;
}

typedef struct { int hit, x, y, z, axis, sgn; float t; } Hit;

/* DDA from o along d (index space, t in index-space units of d). stopWater:
   water counts as a hit. Returns first voxel matching. */
static Hit march(const float o[3], const float d[3], float tStart, int stopWater, int *inWater, float *tWaterIn) {
    Hit h = {0};
    float t0, t1;
    int axis;
    if (!enter(o, d, &t0, &t1, &axis)) return h;
    if (t0 < tStart) t0 = tStart;
    float p[3] = { o[0] + d[0] * (t0 + 1e-4f), o[1] + d[1] * (t0 + 1e-4f), o[2] + d[2] * (t0 + 1e-4f) };
    int v[3] = { (int)floorf(p[0]), (int)floorf(p[1]), (int)floorf(p[2]) };
    int step[3]; float tMax[3], tDelta[3];
    for (int a = 0; a < 3; a++) {
        if (d[a] > 0) { step[a] = 1; tMax[a] = t0 + ((v[a] + 1) - p[a]) / d[a]; tDelta[a] = 1.f / d[a]; }
        else if (d[a] < 0) { step[a] = -1; tMax[a] = t0 + (v[a] - p[a]) / d[a]; tDelta[a] = -1.f / d[a]; }
        else { step[a] = 0; tMax[a] = 1e30f; tDelta[a] = 1e30f; }
    }
    float t = t0;
    for (int i = 0; i < NX + NY + NZ + 8; i++) {
        uint8_t m = at(v[0], v[1], v[2]);
        if (m == 1 && inWater && !*inWater) { *inWater = 1; *tWaterIn = t; }
        if (solid(m) || (stopWater && m == 1)) {
            h.hit = 1; h.x = v[0]; h.y = v[1]; h.z = v[2]; h.axis = axis; h.sgn = -step[axis]; h.t = t;
            return h;
        }
        axis = tMax[0] < tMax[1] ? (tMax[0] < tMax[2] ? 0 : 2) : (tMax[1] < tMax[2] ? 1 : 2);
        t = tMax[axis];
        if (t > t1) break;
        v[axis] += step[axis];
        tMax[axis] += tDelta[axis];
    }
    return h;
}

static uint32_t hash3(int x, int y, int z) {
    uint32_t h = (uint32_t)x * 73856093u ^ (uint32_t)y * 19349663u ^ (uint32_t)z * 83492791u;
    h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
    return h;
}

static void shade(int px, int py) {
    float u = ((px + 0.5f) / W * 2.f - 1.f) * TANH * ((float)W / H);
    float w = (1.f - (py + 0.5f) / H * 2.f) * TANH;
    float dw[3], d[3], o[3];
    for (int a = 0; a < 3; a++) dw[a] = FWD[a] + RIGHT[a] * u + UP[a] * w;
    float len = sqrtf(dw[0] * dw[0] + dw[1] * dw[1] + dw[2] * dw[2]);
    for (int a = 0; a < 3; a++) dw[a] /= len;
    const float s[3] = { SX, SY, SZ };
    for (int a = 0; a < 3; a++) { o[a] = CAM[a] / s[a]; d[a] = dw[a] / s[a]; }   // t stays in metres

    Px *r = &OUT[(size_t)py * W + px];
    memset(r, 0, sizeof(Px));
    r->t = -1.f;
    int inWater = 0; float tw = 0.f;
    Hit h = march(o, d, 0.f, 0, &inWater, &tw);
    if (!h.hit && !inWater) return;
    if (inWater) { r->mat = 1; r->t = tw; r->waterDepth = h.hit ? h.t - tw : 60.f; r->floorMat = h.hit ? at(h.x, h.y, h.z) : 0; }
    if (!h.hit) return;
    if (!inWater) { r->mat = at(h.x, h.y, h.z); r->t = h.t; }
    r->face = (uint8_t)(h.axis * 2 + (h.sgn > 0 ? 0 : 1));
    r->hash = hash3(h.x, h.y, h.z);

    // Ambient occlusion around the air cell in front of the hit face.
    int ax[3] = { h.x, h.y, h.z };
    ax[h.axis] += h.sgn;
    int occ = 0, tot = 0;
    for (int dz = -1; dz <= 1; dz++) for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) {
        if (!dx && !dy && !dz) continue;
        int dd[3] = { dx, dy, dz };
        if (dd[h.axis] == -h.sgn) continue;           // the hit plane's own row: always solid-ish, skip
        tot++;
        if (solid(at(ax[0] + dx, ax[1] + dy, ax[2] + dz))) occ++;
    }
    r->ao = (uint8_t)(255.f * (1.f - (float)occ / (tot > 0 ? tot : 1)));

    // Sun: face N.L, then a shadow ray from the face centre-ish point.
    float n[3] = {0, 0, 0}; n[h.axis] = (float)h.sgn;
    float ndl = n[0] * SUN[0] + n[1] * SUN[1] + n[2] * SUN[2];
    if (ndl <= 0.f) { r->sun = 0.f; return; }
    float hp[3], sd[3];
    for (int a = 0; a < 3; a++) {
        float wp = o[a] + d[a] * h.t;                   // index space hit point
        hp[a] = wp + n[a] * 0.02f;
        sd[a] = SUN[a] / s[a];
    }
    Hit sh = march(hp, sd, 0.f, 0, NULL, NULL);
    r->sun = sh.hit ? 0.f : ndl;
}

typedef struct { int y0, y1; } Job;
static void *worker(void *arg) {
    Job *j = (Job *)arg;
    for (int y = j->y0; y < j->y1; y++) for (int x = 0; x < W; x++) shade(x, y);
    return NULL;
}

int main(int argc, char **argv) {
    if (argc < 21) { fprintf(stderr, "usage: see header\n"); return 2; }
    FILE *f = fopen(argv[1], "rb");
    if (!f) { perror(argv[1]); return 1; }
    char magic[4]; int hdr[8];
    if (fread(magic, 1, 4, f) != 4 || memcmp(magic, "VXV1", 4) || fread(hdr, 4, 8, f) != 8) { fprintf(stderr, "bad vxv\n"); return 1; }
    NX = hdr[0]; NY = hdr[1]; NZ = hdr[2]; STEPXZ = hdr[3]; STEPY = hdr[4];
    size_t n = (size_t)NX * NY * NZ;
    V = malloc(n);
    if (fread(V, 1, n, f) != n) { fprintf(stderr, "short vxv\n"); return 1; }
    fclose(f);
    W = atoi(argv[3]); H = atoi(argv[4]);
    float cam[3] = { atof(argv[5]), atof(argv[6]), atof(argv[7]) }, tgt[3] = { atof(argv[8]), atof(argv[9]), atof(argv[10]) };
    float fov = atof(argv[11]);
    SUN[0] = atof(argv[12]); SUN[1] = atof(argv[13]); SUN[2] = atof(argv[14]);
    CUT = atoi(argv[15]); CUTA = atoi(argv[16]); CUTB = atoi(argv[17]);
    SX = atof(argv[18]); SY = atof(argv[19]); SZ = atof(argv[20]);
    memcpy(CAM, cam, sizeof cam);
    float l = 0; for (int a = 0; a < 3; a++) { FWD[a] = tgt[a] - cam[a]; l += FWD[a] * FWD[a]; }
    l = sqrtf(l); for (int a = 0; a < 3; a++) FWD[a] /= l;
    // Axes: x east, y up, z north (left-handed, like Unity). Facing north
    // (+z), east (+x) is on the right: right = (f.z, 0, -f.x). up is then
    // perpendicular to both with a positive y.
    RIGHT[0] = FWD[2]; RIGHT[1] = 0; RIGHT[2] = -FWD[0];
    l = sqrtf(RIGHT[0] * RIGHT[0] + RIGHT[2] * RIGHT[2]); RIGHT[0] /= l; RIGHT[2] /= l;
    UP[0] = FWD[1] * RIGHT[2] - FWD[2] * RIGHT[1];
    UP[1] = FWD[2] * RIGHT[0] - FWD[0] * RIGHT[2];
    UP[2] = FWD[0] * RIGHT[1] - FWD[1] * RIGHT[0];
    if (UP[1] < 0) { UP[0] = -UP[0]; UP[1] = -UP[1]; UP[2] = -UP[2]; }
    TANH = tanf(fov * 0.5f * 3.14159265f / 180.f);
    OUT = calloc((size_t)W * H, sizeof(Px));
    enum { T = 8 };
    pthread_t th[T]; Job jobs[T];
    for (int i = 0; i < T; i++) { jobs[i].y0 = H * i / T; jobs[i].y1 = H * (i + 1) / T; pthread_create(&th[i], NULL, worker, &jobs[i]); }
    for (int i = 0; i < T; i++) pthread_join(th[i], NULL);
    FILE *o = fopen(argv[2], "wb");
    fwrite(OUT, sizeof(Px), (size_t)W * H, o);
    fclose(o);
    return 0;
}
