import sys, os
T=os.environ.get('T','exact')
from PIL import Image, ImageChops
d='Screenshots/201a/'
for pose in ['forest','town_eye','s_redwood_spacecol']:
    try:
        A=Image.open(f'{d}{pose}_native_sf0_gs1_{T}.png' if T!='exact' and T!='exact2' else f'{d}{pose}_native_sf0_{T}.png').convert('RGB')
        B=Image.open(f'{d}{pose}_native_sf0_gs0_{T}.png').convert('RGB')
        C=Image.open(f'{d}{pose}_{T}.png').convert('RGB')
    except Exception as e:
        print(pose,'missing',e); continue
    def n(x,y):
        df=ImageChops.difference(x,y); bb=df.getbbox()
        cnt=sum(1 for p in df.getdata() if p!=(0,0,0))
        return f'{cnt} px differ (bbox {bb})'
    print(f'{pose}: seal ON vs OFF {n(A,B)} | OFF vs ON-again {n(B,C)} | ON vs ON-again (control) {n(A,C)}  size {A.size}')
