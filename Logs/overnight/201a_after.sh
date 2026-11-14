#!/bin/bash
# Task 201a after the fix: (1) everything ON (sealed bricks, default) -- build; (2) -trees 0 -towns 0 -gpuseal 0
# (the pre-fix upload without trees/towns, accounting step 1); (3) exactness: big pool so neither arm refuses,
# 3 poses x arms native_sf0 (seal ON) / native_sf0_gs0 (seal OFF, box restaged) / native (seal ON again = control).
cd "/Users/hritvikjv/Desktop/Personal Projects/Unity/Voxel Terraria 1 Byte BrickMap"
SB=0 TAG=sealed Logs/overnight/201a_account.sh
SB=1 TAG=notrees XF="-trees 0 -towns 0 -gpuseal 0" Logs/overnight/201a_account.sh
SB=1 TAG=exact XF="-gpubrickcap 420000 -wsarms native_sf0,native_sf0_gs0,native -wsposes fixed" \
  P3=1 Logs/overnight/201a_account.sh
echo "=== AFTER ALL DONE $(date +%T)"
