# task 99: pixel diff of two stills (count of differing pixels, max channel delta).
import sys
import numpy as np
from PIL import Image
a = np.asarray(Image.open(sys.argv[1]).convert('RGB')).astype(int)
b = np.asarray(Image.open(sys.argv[2]).convert('RGB')).astype(int)
d = np.abs(a - b).max(axis=2)
n = int((d > 0).sum())
print(f"{sys.argv[1].split('/')[-1]} vs {sys.argv[2].split('/')[-1]}: {n} px differ ({100.0 * n / d.size:.4f}%), max delta {int(d.max())}")
