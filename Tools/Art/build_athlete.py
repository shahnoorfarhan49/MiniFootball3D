"""Generate the original shared footballer, skeleton, skin weights and two mesh LODs.

Run blender -b --factory-startup --python Tools/Art/build_athlete.py from the project root.
Geometry is lofted from anatomical cross sections in metres. Forward is Blender -Y.
"""
from pathlib import Path
import math
import bpy
from mathutils import Vector

output = Path(__file__).resolve().parents[2] / "Assets/MiniFootball/Models/Footballer.fbx"
bpy.ops.wm.read_factory_settings(use_empty=True)
rig_data = bpy.data.armatures.new("PlayerSkeleton")
rig = bpy.data.objects.new("PlayerSkeleton", rig_data)
bpy.context.collection.objects.link(rig)
bpy.context.view_layer.objects.active = rig
rig.select_set(True)
bpy.ops.object.mode_set(mode='EDIT')

def bone(name, head, tail, parent=None):
    b = rig_data.edit_bones.new(name)
    b.head, b.tail = head, tail
    if parent:
        b.parent = rig_data.edit_bones[parent]
        b.use_connect = (b.head - b.parent.tail).length < .00001

bone("Hips", (0,0,.91), (0,0,1.04))
bone("Spine", (0,0,1.04), (0,0,1.24), "Hips")
bone("Chest", (0,0,1.24), (0,0,1.46), "Spine")
bone("Neck", (0,0,1.46), (0,-.005,1.555), "Chest")
bone("Head", (0,-.005,1.555), (0,-.002,1.81), "Neck")
for sign, side in [(1,"Left"),(-1,"Right")]:
    def coord(x,y,z): return (x*sign,y,z)
    bone(side+"Shoulder", coord(.025,0,1.435), coord(.196,0,1.445), "Chest")
    bone(side+"UpperArm", coord(.196,0,1.445), coord(.472,-.003,1.445), side+"Shoulder")
    bone(side+"LowerArm", coord(.472,-.003,1.445), coord(.705,-.012,1.445), side+"UpperArm")
    bone(side+"Hand", coord(.705,-.012,1.445), coord(.854,-.012,1.445), side+"LowerArm")
    bone(side+"UpperLeg", coord(.092,0,.925), coord(.10,-.024,.497), "Hips")
    bone(side+"LowerLeg", coord(.10,-.024,.497), coord(.105,0,.095), side+"UpperLeg")
    bone(side+"Foot", coord(.105,0,.095), coord(.105,-.145,.04), side+"LowerLeg")
    bone(side+"Toes", coord(.105,-.145,.04), coord(.105,-.22,.04), side+"Foot")
bpy.ops.object.mode_set(mode='OBJECT')
rig.select_set(False)

# Texture palette indices are shared with AthleteAppearance.cs.
SKIN, JERSEY, SHORTS, SOCK, BOOT, HAIR, WHITE, IRIS, LIP, TRIM, BROW, SOLE, SEAM, EAR, METAL, CREST = range(16)

class Surface:
    def __init__(self, name, detail):
        self.name, self.detail = name, detail
        self.verts, self.weights, self.faces, self.uvs = [], [], [], []
    def v(self, point, weights):
        self.verts.append(point)
        self.weights.append({weights:1} if isinstance(weights,str) else weights)
        return len(self.verts)-1
    def f(self, indices, tile, uv=None):
        self.faces.append(tuple(indices))
        uv = uv or [(0,0),(1,0),(1,1),(0,1)][:len(indices)]
        if tile is None:
            self.uvs.append([(max(.002,min(.998,u)),.503+v*.49) for u,v in uv])
        else:
            self.uvs.append([((tile%8+.06+u*.88)/8,(tile//8+.06+v*.88)/4) for u,v in uv])
    def loft(self, rings, tile, weights, count=14, angle_fn=None):
        # Each ring specifies center x/y/z, horizontal radius x, sagittal radius y.
        count=max(6,round(count*self.detail))
        grid=[]
        for i,(x,y,z,rx,ry) in enumerate(rings):
            row=[]
            for j in range(count):
                angle=2*math.pi*j/count
                point=(x+rx*math.cos(angle),y+ry*math.sin(angle),z)
                if angle_fn: point=angle_fn(i,angle,point)
                row.append(self.v(point,weights[i] if isinstance(weights,list) else weights))
            grid.append(row)
        for i in range(len(grid)-1):
            for j in range(count):
                k=(j+1)%count
                self.f([grid[i][j],grid[i][k],grid[i+1][k],grid[i+1][j]],tile,
                       [(j/count,i/(len(grid)-1)),((j+1)/count,i/(len(grid)-1)),
                        ((j+1)/count,(i+1)/(len(grid)-1)),(j/count,(i+1)/(len(grid)-1))])
        for i,reverse in [(0,True),(-1,False)]:
            coords=[self.verts[v] for v in grid[i]]
            center=self.v(tuple(sum(c[d] for c in coords)/len(coords) for d in range(3)),self.weights[grid[i][0]])
            for j in range(count):
                ids=[center,grid[i][j],grid[i][(j+1)%count]]
                self.f(ids[::-1] if reverse else ids,tile)
        return grid
    def arm(self, rings, tile, weights, sign, count=10):
        # Loft along the local lateral axis. Rotate the section rather than scaling a primitive.
        start=len(self.verts)
        self.loft([(z,y,x,rz,ry) for x,y,z,ry,rz in rings],tile,weights,count)
        for i in range(start,len(self.verts)):
            z,y,x=self.verts[i]
            self.verts[i]=(sign*x,y,z)
        if sign>0: # swapping x and z flips the winding
            self.reverse_from(start)
    def reverse_from(self,start):
        for n in range(len(self.faces)-1,-1,-1):
            if self.faces[n][0]<start: break
            self.faces[n]=self.faces[n][::-1]; self.uvs[n]=self.uvs[n][::-1]
    def complete(self):
        mesh=bpy.data.meshes.new(self.name)
        mesh.from_pydata(self.verts,[],self.faces); mesh.update()
        uv=mesh.uv_layers.new(name="TeamAtlas")
        for poly,points in zip(mesh.polygons,self.uvs):
            poly.use_smooth=True
            for li,point in zip(poly.loop_indices, points): uv.data[li].uv=point
        obj=bpy.data.objects.new(self.name,mesh); bpy.context.collection.objects.link(obj)
        obj.parent=rig
        groups={b.name:obj.vertex_groups.new(name=b.name) for b in rig_data.bones}
        for i,weights in enumerate(self.weights):
            assert abs(sum(weights.values())-1)<.0001
            for name,w in weights.items(): groups[name].add([i],w,'REPLACE')
        m=obj.modifiers.new("Player Skin",'ARMATURE'); m.object=rig
        return obj

def mix(a,b,t): return {a:1-t,b:t}

def body(detail, name):
    s=Surface(name,detail)
    # Tailored jersey: chest, narrow waist, sloped shoulders, bound neckline.
    torso=[(.935,.173,.108),(.958,.178,.107),(1.025,.163,.099),(1.10,.151,.090),
           (1.195,.161,.107),(1.283,.18,.120),(1.36,.193,.123),(1.405,.197,.102),
           (1.442,.173,.079),(1.468,.082,.058)]
    tw=["Hips","Hips",mix("Hips","Spine",.45),"Spine",mix("Spine","Chest",.40),
        "Chest","Chest","Chest","Chest","Chest"]
    def torso_shape(i,a,p):
        x,y,z=p
        # Chest fullness and trapezius slope over the shoulder.
        return x,y-(.008 if i in (5,6) else 0)*max(0,-math.sin(a)),z + (.01 if i == 8 else 0) * abs(math.cos(a))**2
    jersey=s.loft([(0,0,z,rx,ry) for z,rx,ry in torso],JERSEY,tw,24,torso_shape)
    # Cut an armhole into each side of the shirt. Stitch each sleeve to its boundary
    # so the clavicle and deltoid remain one surface as the arms lower from the T-pose.
    count=len(jersey[0])
    remove=set()
    armholes={}
    for side,center in [(1,0),(-1,count//2)]:
        span=max(1,round(count/8))
        sides=list(range(center-span,center+span+1))
        for i in range(5,8):
            remove.update(i*count+j%count for j in sides[:-1])
        loop=[jersey[5][j%count] for j in sides]
        loop += [jersey[i][sides[-1]%count] for i in range(6,9)]
        loop += [jersey[8][j%count] for j in reversed(sides[:-1])]
        loop += [jersey[i][sides[0]%count] for i in (7,6)]
        def angle(v):
            x,y,z=s.verts[v]
            return math.atan2(y/.09,(z-1.366)/.078)
        loop.sort(key=angle)
        armholes[side]=[(v,angle(v)) for v in loop]
    s.faces=[f for i,f in enumerate(s.faces) if i not in remove]
    s.uvs=[uv for i,uv in enumerate(s.uvs) if i not in remove]
    s.loft([(0,0,1.452,.092,.067),(0,0,1.471,.078,.061)],TRIM,"Chest",18)
    s.loft([(0,0,.929,.171,.104),(0,0,.947,.177,.11)],TRIM,"Hips",22)
    s.loft([(0,.003,.852,.167,.102),(0,.005,.92,.177,.113),(0,0,.952,.170,.108)],SHORTS,"Hips",22)
    s.loft([(0,0,1.432,.066,.061),(0,-.003,1.485,.055,.054),(0,-.005,1.55,.059,.061),
            (0,-.012,1.581,.060,.058)],SKIN,["Chest","Neck","Neck","Head"],16)
    # A curved back print follows the shirt's cross sections and spine weights.
    levels=[(1.07,.115,.156,.097,mix("Spine","Hips",.16)),(1.155,.122,.158,.103,"Spine"),
            (1.235,.125,.174,.119,mix("Spine","Chest",.68)),(1.308,.123,.186,.125,"Chest"),
            (1.356,.126,.193,.126,"Chest")]
    rows=[]
    for z,span,shirt_x,shirt_y,w in levels:
        rows.append([s.v((x,shirt_y*math.sqrt(max(.01,1-(x/shirt_x)**2))+.004,z),w)
                     for x in [span*(i/4-1) for i in range(9)]])
    for i in range(len(rows)-1):
        for j in range(8):
            u0,u1=1-j/8,1-(j+1)/8
            v0,v1=(levels[i][0]-1.07)/.286,(levels[i+1][0]-1.07)/.286
            s.f([rows[i][j],rows[i+1][j],rows[i+1][j+1],rows[i][j+1]],None,[(u0,v0),(u0,v1),(u1,v1),(u1,v0)])
    # Small stitched chest patch and a slanted inset. Both panels follow the jersey's
    # chest bone, with a curved backing along the shirt rather than a flat floating tag.
    def chest_patch(outline, tile, depth):
        verts = []
        for x, z in outline:
            lo = next(i for i in range(len(torso)-1) if torso[i][0] <= z <= torso[i+1][0])
            u = (z-torso[lo][0]) / (torso[lo+1][0]-torso[lo][0])
            rx, ry = [(1-u)*torso[lo][j]+u*torso[lo+1][j] for j in (1,2)]
            y = -math.sqrt(max(0, 1-(x/rx)**2)) * (ry + .008)
            verts.append(s.v((x,y-depth,z), "Chest"))
        for i in range(1,len(verts)-1):
            s.f([verts[0],verts[i],verts[i+1]],tile)
    chest_patch([(.056,1.336),(.07,1.327),(.087,1.335),(.091,1.36),(.057,1.36)], CREST,.002)
    chest_patch([(.060,1.340),(.063,1.335),(.083,1.350),(.085,1.357)], JERSEY,.003)
    # Sculpted face sections from chin to crown, including jaw planes, brow, cheekbones and nasal bridge.
    head=[(1.531,.038,.052,-.031),(1.55,.057,.068,-.025),(1.577,.073,.078,-.012),(1.609,.082,.087,-.006),
          (1.637,.085,.091,-.006),(1.657,.084,.090,-.005),(1.686,.086,.091,-.004),
          (1.720,.086,.091,.001),(1.751,.077,.087,.005),(1.778,.056,.068,.008),(1.789,.022,.032,.011)]
    def face_shape(i,a,p):
        x,y,z=p
        front=max(0,-math.sin(a))
        if i==5: y+=.005*front*min(1,abs(x)/.03)
        if i in [1,2,3]: x*=.95+.05*abs(math.cos(a))
        if i in [3,4]: y-=.004*front*min(1,abs(x)/.032)
        return x,y,z
    s.loft([(0,y,z,rx,ry) for z,rx,ry,y in head],SKIN,"Head",28,face_shape)
    s.loft([(0,-.092,1.616,.011,.005),(0,-.101,1.622,.018,.014),(0,-.104,1.630,.013,.015),
            (0,-.093,1.652,.008,.009),(0,-.090,1.677,.007,.005)],SKIN,"Head",12)
    for sign, side in [(1,"Left"),(-1,"Right")]:
        U,L,H=side+"UpperArm",side+"LowerArm",side+"Hand"
        UL,LL,F,T=side+"UpperLeg",side+"LowerLeg",side+"Foot",side+"Toes"
        def leg(rings, tile, ws):
            s.loft([(x*sign,y,z,rx,ry) for z,x,y,rx,ry in rings],tile,ws,16)
        leg([(.665,.097,0,.079,.086),(.687,.095,.002,.086,.093),(.79,.097,.005,.091,.105),
             (.866,.09,.004,.091,.109),(.917,.09,.002,.084,.098)],SHORTS,[UL,UL,UL,mix(UL,"Hips",.38),"Hips"])
        leg([(.66,.097,0,.080,.086),(.675,.097,0,.082,.088)],TRIM,UL)
        leg([(.415,.101,-.008,.047,.052),(.463,.101,-.017,.053,.059),(.5,.10,-.023,.057,.062),
             (.55,.099,-.011,.056,.067),(.619,.099,0,.065,.078),(.695,.097,0,.074,.085)],
            SKIN,[LL,mix(LL,UL,.28),mix(UL,LL,.50),UL,UL,UL])
        leg([(.10,.105,.004,.034,.042),(.153,.104,.009,.032,.041),(.227,.103,.015,.040,.052),
             (.322,.102,.020,.053,.065),(.377,.101,.008,.055,.062),(.427,.101,-.006,.047,.053)],SOCK,LL)
        leg([(.403,.101,-.003,.050,.056),(.422,.101,-.005,.05,.056)],TRIM,LL)
        # Shaped boot upper and sole. The toe projects forward and the heel sits behind the ankle.
        leg([(.024,.105,-.068,.055,.139),(.039,.105,-.071,.057,.141),(.062,.105,-.069,.057,.134),
             (.086,.105,-.049,.05,.109),(.114,.105,-.013,.04,.064),(.125,.105,.002,.035,.047)],
            BOOT,[F,F,F,F,mix(F,LL,.15),mix(F,LL,.3)])
        leg([(.015,.105,-.069,.055,.140),(.030,.105,-.069,.058,.143)],SOLE,F)
        if detail>.6:
            for n in range(3):
                yy=-.062-n*.018
                leg([(.089-n*.004,.105,yy,.026,.003),(.092-n*.004,.105,yy,.026,.003)],WHITE,F)
        hole=armholes[sign]
        previous=[v for v,a in hole]
        for x,z,ry,rz,weight,tile in [
            (.198,1.391,.082,.067,mix(side+"Shoulder",U,.25),JERSEY),
            (.228,1.411,.073,.058,mix(side+"Shoulder",U,.64),JERSEY),
            (.261,1.431,.065,.056,U,JERSEY),(.296,1.436,.057,.054,U,JERSEY),
            (.311,1.436,.055,.053,U,TRIM)]:
            row=[s.v((sign*x,ry*math.sin(a),z+rz*math.cos(a)),weight) for _,a in hole]
            for j in range(len(row)):
                k=(j+1)%len(row)
                quad=[previous[j],row[j],row[k],previous[k]]
                s.f(quad if sign>0 else quad[::-1],tile)
            previous=row
        s.arm([(.294,0,1.438,.049,.05),(.346,-.002,1.439,.049,.055),(.408,0,1.442,.040,.044),
               (.454,.001,1.444,.031,.037),(.479,-.003,1.445,.033,.033),(.52,-.004,1.444,.042,.039),
               (.579,-.006,1.444,.041,.035),(.649,-.01,1.445,.032,.027),(.707,-.012,1.445,.025,.020)],
              SKIN,[U,U,U,mix(U,L,.22),mix(U,L,.65),L,L,L,mix(L,H,.20)],sign,14)
        s.arm([(.691,-.011,1.445,.023,.019),(.72,-.015,1.446,.032,.019),(.754,-.017,1.443,.041,.021),
               (.783,-.017,1.443,.036,.016)],SKIN,[L,H,H,H],sign)
        for j in range(4):
            y=-.044+j*.0175
            end=(.852,.86,.851,.833)[j]
            s.arm([(.772,y,1.444,.011,.014),(.806,y+(j-1.5)*.001,1.444,.009,.011),
                   (end,y+(j-1.5)*.003,1.439,.006,.007)],SKIN,H,sign,8)
        s.arm([(.726,-.035,1.443,.014,.016),(.752,-.061,1.439,.013,.014),(.784,-.074,1.434,.008,.009)],SKIN,H,sign,8)
        # Pinched ear helix, recessed concha, inset eye, eyelid, iris and brow.
        def feature(rows,tile,n=12):
            s.loft([(sign*x,y,z,rx,ry) for z,x,y,rx,ry in rows],tile,"Head",n)
        feature([(1.612,.08,-.003,.003,.005),(1.622,.085,-.004,.013,.013),(1.648,.088,.001,.014,.020),
                 (1.669,.085,.004,.012,.015),(1.675,.081,.004,.006,.007)],SKIN)
        feature([(1.63,.096,-.007,.003,.004),(1.644,.099,-.004,.003,.011),(1.657,.097,0,.003,.005)],EAR)
        # Eyelids wrap the eye around the skull at the temple. The visible sclera
        # tapers to a corner on both sides and carries a rounded iris and a pupil.
        def eye_vertex(dx, dz, extra_depth=0):
            x = .033 + dx
            y = -.005 - .090 * math.sqrt(max(.05, 1 - (x/.084)**2))
            y -= .003 + .0017 * max(0, 1 - (dx/.02)**2) + extra_depth
            return s.v((sign*x, y, 1.657 + dz + dx*.035), "Head")
        outline = [(-.017,0),(-.011,-.0034),(0,-.0041),(.010,-.0034),(.016,0),
                   (.009,.0041),(0,.0052),(-.010,.0043)]
        rim = [eye_vertex(x,z) for x,z in outline]
        lid = [eye_vertex(x*1.12,z*1.5) for x,z in outline]
        center = eye_vertex(0, 0, .001)
        for i in range(len(rim)):
            j=(i+1)%len(rim)
            tri=[rim[i],rim[j],center]
            quad=[rim[i],lid[i],lid[j],rim[j]]
            s.f(tri if sign>0 else tri[::-1],WHITE)
            s.f(quad if sign>0 else quad[::-1],SKIN)
        def eye_disk(rx, rz, depth, tile):
            center = eye_vertex(-.001, .00015, depth)
            ring = [eye_vertex(-.001+rx*math.cos(a*math.tau/12),
                               .00015+rz*math.sin(a*math.tau/12),depth) for a in range(12)]
            for i in range(12):
                tri=[center,ring[i],ring[(i+1)%12]]
                s.f(tri if sign>0 else tri[::-1],tile)
        eye_disk(.0057,.0044,.002,IRIS)
        eye_disk(.0027,.0028,.0025,HAIR)
        # Trace the arched brow over the socket, narrowing to a point at the temple.
        browline = [(-.016,.012),(-.006,.015),(.006,.0157),(.016,.013),(.019,.012)]
        pairs = []
        for i,(dx,dz) in enumerate(browline):
            half = (.0019,.0027,.0025,.0017,.00035)[i]
            pairs.append([eye_vertex(dx,dz-half,.0018),eye_vertex(dx,dz+half,.0018)])
        for a,b in zip(pairs,pairs[1:]):
            quad=[a[0],b[0],b[1],a[1]]
            s.f(quad if sign>0 else quad[::-1],BROW)
    def lips(x):
        y=-.006-.083*math.sqrt(1-(x/.079)**2)
        t=abs(x/.023)
        center=1.593+.0016*t
        bulge=(1-t)**.6
        return [s.v((x,y-.001,center-.004*bulge),"Head"),
                s.v((x,y-.0039*bulge-.001,center-.001*bulge),"Head"),
                s.v((x,y-.002*bulge-.001,center),"Head"),
                s.v((x,y-.004*bulge-.001,center+.0026*bulge),"Head"),
                s.v((x,y-.001,center+.0048*bulge+.0017*math.exp(-((t-.32)*7)**2)),"Head")]
    mouth=[lips(.023*(i/5-1)) for i in range(11)]
    for a,b in zip(mouth,mouth[1:]):
        for i in range(4): s.f([a[i],b[i],b[i+1],a[i+1]], LIP)
    obj = s.complete()
    basis = obj.shape_key_add(name="Basis")
    jaw = obj.shape_key_add(name="BroadJaw")
    cheeks = obj.shape_key_add(name="Cheekbones")
    for i, weights in enumerate(s.weights):
        if "Head" not in weights:
            continue
        x, y, z = basis.data[i].co
        # Keep the ear, eye and lip meshes aligned with the facial surface.
        jaw_falloff = max(0, 1 - abs(z - 1.581) / .068)
        cheek_falloff = max(0, 1 - abs(z - 1.632) / .043) * min(1, abs(x) / .044)
        jaw.data[i].co.x += x * .17 * jaw_falloff
        cheeks.data[i].co.x += x * .105 * cheek_falloff
        if y < 0:
            cheeks.data[i].co.y -= .007 * cheek_falloff
    return obj

def hairstyle(index):
    s=Surface("HairStyle"+str(index),1)
    # Hair follows the skull with a separate fitted hairline, sideburn, and crown.
    # Offset each ring along a different growth direction for each of the four cuts.
    rows=[(1.637,.090,.094),(1.658,.092,.099),(1.693,.095,.103),
          (1.719,.096,.104),(1.746,.090,.099),(1.767,.079,.091),
          (1.783,.063,.077),(1.793,.041,.057),(1.798,.017,.029)]
    def scalp_point(i,a):
        z,rx,ry=rows[i]
        t=i/(len(rows)-1)
        front=max(0,-math.sin(a))
        # An M-shaped hairline sits higher at the temples than the forehead's centre.
        temples=math.exp(-((abs(math.cos(a))-.72)/.19)**2)*front
        seam=(.054*front+.023*temples)*max(0,1-t/.5)
        if index==1:
            seam-=.014*front*max(0,math.cos(a))*max(0,1-t/.45)
        if index==2:
            seam-=.007*max(0,1-t/.33)*(1+math.cos(a*12))
        wave=math.cos(14*a+3*t)
        if index==0: ripple=.0007*wave
        elif index==2: ripple=.0036*math.cos(18*a-4*t)*math.cos(2*math.pi*t)
        else: ripple=.0021*math.cos(12*a+t*(4 if index==1 else -5))
        dx=(.009 if index==1 else -.007 if index==3 else 0)*t*t
        crown=(.013 if index==1 else .011 if index==2 else .008 if index==3 else 0)*t
        x=(rx+ripple)*math.cos(a)+dx
        y=.004 + (ry+ripple)*math.sin(a)
        return x,y,z+seam+crown
    s.loft([(0,0,z,rx,ry) for z,rx,ry in rows],HAIR,"Head",52,
           lambda i,a,p:scalp_point(i,a))
    # Individual tapered locks follow the crown. They share the character atlas and
    # deform with the rest of the hairstyle, adding detail with a single material.
    nlocks=18 if index==2 else 14
    for lock in range(nlocks):
        start=1+lock%3
        end=7 if lock%2==0 else 6
        previous=None
        for i in range(start,end+1):
            u=(i-start)/(end-start)
            theta=math.tau*lock/nlocks + (i-start)*(.022 if index==1 else -.027 if index==3 else .009)
            width=.002+.015*math.sin(math.pi*u)
            pair=[]
            for offset in [-width,width]:
                a=theta+offset
                x,y,z=scalp_point(i,a)
                pair.append(s.v((x+.0013*math.cos(a),y+.0013*math.sin(a),z+.0008),"Head"))
            if previous is not None:
                s.f([previous[0],previous[1],pair[1],pair[0]],BROW)
            previous=pair
    return s.complete()

body(1,"AthleteNear")
body(.46,"AthleteFar")
for style in range(4): hairstyle(style)
bpy.ops.object.select_all(action='SELECT')
for obj in bpy.context.selected_objects:
    if obj.type=='MESH':
        assert all(v.groups for v in obj.data.vertices), obj.name
        print(f"{obj.name}: {len(obj.data.vertices)} vertices")
bpy.ops.export_scene.fbx(filepath=str(output),use_selection=True,object_types={'ARMATURE','MESH'},
    global_scale=1,apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',axis_forward='-Z',axis_up='Y',
    add_leaf_bones=False,bake_anim=False,mesh_smooth_type='FACE',use_tspace=False,path_mode='STRIP')
