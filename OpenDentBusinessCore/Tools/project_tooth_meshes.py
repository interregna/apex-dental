#!/usr/bin/env python3
"""
One-time mesh projection job (workplan §4 step 6): parse the Wavefront .obj
tooth meshes that ship inside SparksToothChart, project every surface group to
the facial (X/Y) plane, and emit simplified 2D outlines as JSON for the
Linux-core tooth chart renderers.

Method: per group, scanline-rasterize the projected triangles into a mask,
trace connected-component boundaries (Moore neighbor tracing), then simplify
with Ramer-Douglas-Peucker. Output coordinates are in "image" space: origin
top-left, y increases downward, root apex at the top (the .obj files put the
root toward +Y).

Usage:  python3 project_tooth_meshes.py <repo_root> <out_json>
"""
import json,math,os,sys

GRID_W=160
RDP_EPS=1.2

GROUP_MAP={
    "cube1_Enamel2":"Enamel","cube1_Cementum":"Cementum","cube1_Buildup":"Buildup",
    "cube1_M":"M","cube1_O":"O","cube1_D":"D","cube1_B":"B","cube1_L":"L",
    "cube1_F":"F","cube1_I":"I","cube1_V":"V","cube1_EnamelF":"EnamelF",
    "cube1_DF":"DF","cube1_MF":"MF","cube1_IF":"IF",
}

def parse_obj(path):
    verts=[];groups={};cur=None
    for line in open(path,encoding="utf-8",errors="ignore"):
        if line.startswith("v "):
            p=line.split();verts.append((float(p[1]),float(p[2])))
        elif line.startswith("g "):
            name=line.strip()[2:].strip()
            cur=GROUP_MAP.get("cube1_"+name.split("cube1_")[-1] if "cube1_" in name else name,
                              GROUP_MAP.get(name))
            if cur is None and name!="cube1_default":
                cur=None
            groups.setdefault(cur,[]) if cur else None
        elif line.startswith("f ") and cur:
            idx=[int(t.split("/")[0])-1 for t in line.split()[1:]]
            for i in range(1,len(idx)-1):
                groups[cur].append((idx[0],idx[i],idx[i+1]))
    return verts,groups

def rasterize(tris,verts,minx,miny,scale,w,h):
    mask=[[0]*w for _ in range(h)]
    for a,b,c in tris:
        pts=[((verts[i][0]-minx)*scale,(verts[i][1]-miny)*scale) for i in (a,b,c)]
        ys=[p[1] for p in pts]
        y0=max(0,int(math.floor(min(ys))));y1=min(h-1,int(math.ceil(max(ys))))
        for y in range(y0,y1+1):
            xs=[]
            for i in range(3):
                (x1,yy1),(x2,yy2)=pts[i],pts[(i+1)%3]
                if (yy1<=y<yy2) or (yy2<=y<yy1):
                    xs.append(x1+(y-yy1)*(x2-x1)/(yy2-yy1))
            xs.sort()
            for i in range(0,len(xs)-1,2):
                for x in range(max(0,int(math.floor(xs[i]))),min(w-1,int(math.ceil(xs[i+1])))+1):
                    mask[y][x]=1
    return mask

def trace_contours(mask,w,h):
    """All outer boundaries via Moore neighbor tracing per connected component."""
    seen=[[False]*w for _ in range(h)]
    def at(x,y): return 0<=x<w and 0<=y<h and mask[y][x]
    contours=[]
    NB=[(1,0),(1,1),(0,1),(-1,1),(-1,0),(-1,-1),(0,-1),(1,-1)]
    for sy in range(h):
        for sx in range(w):
            if not mask[sy][sx] or seen[sy][sx]:
                continue
            if at(sx-1,sy):#not a left boundary start
                seen[sy][sx]=True
                continue
            #Moore tracing from (sx,sy), backtrack initially points left.
            contour=[];x,y=sx,sy;bx,by=sx-1,sy
            start=(x,y);startb=(bx,by);first=True
            while first or (x,y)!=start or (bx,by)!=startb:
                first=False
                contour.append((x,y));seen[y][x]=True
                bi=NB.index((bx-x,by-y))
                for k in range(1,9):
                    dx,dy=NB[(bi+k)%8]
                    nx,ny=x+dx,y+dy
                    if at(nx,ny):
                        px,py=NB[(bi+k-1)%8]
                        bx,by=x+px,y+py
                        x,y=nx,ny
                        break
                else:
                    break#isolated pixel
                if len(contour)>40000:
                    break
            if len(contour)>=8:
                contours.append(contour)
            #flood-mark component so we don't restart inside it
            stack=[(sx,sy)]
            while stack:
                cx,cy=stack.pop()
                if not(0<=cx<w and 0<=cy<h) or seen[cy][cx] and (cx,cy)!=(sx,sy):
                    pass
                if 0<=cx<w and 0<=cy<h and mask[cy][cx] and not seen[cy][cx]:
                    seen[cy][cx]=True
                    stack.extend([(cx+1,cy),(cx-1,cy),(cx,cy+1),(cx,cy-1)])
    return contours

def rdp(points,eps):
    if len(points)<3:
        return points
    def dist(p,a,b):
        ax,ay=a;bx,by=b;px,py=p
        dx,dy=bx-ax,by-ay
        if dx==dy==0:
            return math.hypot(px-ax,py-ay)
        t=max(0,min(1,((px-ax)*dx+(py-ay)*dy)/(dx*dx+dy*dy)))
        return math.hypot(px-(ax+t*dx),py-(ay+t*dy))
    dmax,idx=0,0
    for i in range(1,len(points)-1):
        d=dist(points[i],points[0],points[-1])
        if d>dmax:
            dmax,idx=d,i
    if dmax>eps:
        left=rdp(points[:idx+1],eps);right=rdp(points[idx:],eps)
        return left[:-1]+right
    return [points[0],points[-1]]

def main():
    repo=sys.argv[1];out=sys.argv[2]
    src=os.path.join(repo,"SparksToothChart","Resources")
    result={}
    for fn in sorted(os.listdir(src)):
        if not fn.endswith(".obj"):
            continue
        key=fn[:-4]
        verts,groups=parse_obj(os.path.join(src,fn))
        if not verts:
            continue
        xs=[v[0] for v in verts];ys=[v[1] for v in verts]
        minx,maxx,miny,maxy=min(xs),max(xs),min(ys),max(ys)
        scale=GRID_W/max(1e-6,(maxx-minx))
        w=GRID_W+2;h=int((maxy-miny)*scale)+2
        entry={"w":round(maxx-minx,3),"h":round(maxy-miny,3),"groups":{}}
        for gname,tris in groups.items():
            if not gname or not tris:
                continue
            mask=rasterize(tris,verts,minx,miny,scale,w,h)
            contours=trace_contours(mask,w,h)
            outlines=[]
            for contour in contours:
                simplified=rdp(contour,RDP_EPS)
                if len(simplified)>=4:
                    #to model units, y flipped so root(+Y) is at top of image space
                    outlines.append([[round((px/scale),2),round(((h-2-py)/scale),2)] for px,py in simplified])
            if outlines:
                entry["groups"][gname]=outlines
        result[key]=entry
        n=sum(len(o) for g in entry["groups"].values() for o in g)
        print(f"{key}: {len(entry['groups'])} groups, {n} pts")
    with open(out,"w") as f:
        json.dump(result,f,separators=(",",":"))
    print("wrote",out,os.path.getsize(out),"bytes")

if __name__=="__main__":
    main()
