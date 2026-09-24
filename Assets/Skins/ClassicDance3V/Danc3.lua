function Init()

	mjudge={}
	mjudge[1]=Module:Find("Perfect")
	mjudge[2]=Module:Find("Great")
	mjudge[3]=Module:Find("Good")
	mjudge[4]=Module:Find("Miss")
	combo = Module:Find("ComboNum")
	combotag = Module:Find("ComboTag")
	mjudgepos={x={-150,-350,-150,150,350,150},y={350,50,-350,-350,50,350}}

    sjudge={}
    sjudge2={{},{},{},{}}
    for i=1,4 do
        sjudge[i]=Module:Find("judge2"..i)
    end
    for i=1,4 do
        for k=1,5 do
            sjudge2[i][k]=Module:Clone(sjudge[i],"sjudge2"..i..k)
        end
    end
    sjudge3={}
    judgenum={}
	movetime=-1000
    m=6

    dline=Module:Find("Doubleline")
    dlinepos={x={{nil,-326.25,-217.5,0,108.75,0},{nil,nil,-326.25,-108.75,0,-108.75},{nil,nil,nil,0,108.75,0},{nil,nil,nil,nil,326.25,217.5},{nil,nil,nil,nil,nil,326.25}},y={{nil,188.36,0,0,188.36,376.72},{nil,nil,-188.36,-188.36,0,188.36},{nil,nil,nil,-376.72,-188.36,0},{nil,nil,nil,nil,-188.36,0},{nil,nil,nil,nil,nil,188.36}},r={{nil,300,270,240,30,180},{nil,nil,240,210,180,330},{nil,nil,nil,180,150,120},{nil,nil,nil,nil,120,90},{nil,nil,nil,nil,nil,60}},w={{nil,400,740,860,740,400},{nil,nil,400,740,860,740},{nil,nil,nil,400,740,860},{nil,nil,nil,nil,400,740},{nil,nil,nil,nil,nil,400}}}
    


    hitline={{},{},{},{},{},{}}
    for i=1,6 do
        for k=1,6 do
            if (k>i) then
                hitline[i][k]=Module:Clone(dline,"hitline"..i..k)
                hitline[i][k].X=dlinepos.x[i][k]
                hitline[i][k].Y=dlinepos.y[i][k]
                hitline[i][k].Rotate=dlinepos.r[i][k]
                hitline[i][k].Width=dlinepos.w[i][k]
                hitline[k][i]=hitline[i][k]
            end
        end
    end
    cube=1
    notetime={}
    shit={}
    shitx={}
end

function OnRetry()
    m=6
    cube=1
    sjudge3={}
    judgenum={}
    movetime=-1000
end

function Update()
    uptime=Game:Time()
    if (#judgenum>5) then
        table.remove(judgenum,1)
    end
    if (#judgenum>0) then
        for i=1,4 do
            if (judgenum[1]==i) then
                if (uptime>movetime) then
                    movetime=math.floor(uptime/250)*250+250
                    
                    sjudge3[(m-1)%5+1]=sjudge2[i][(m-1)%5+1]
                    sjudge3[(m-1)%5+1].X,sjudge3[(m-1)%5+1].Y=500,-140
                    sjudge3[(m-1)%5+1]:DoMoveX({start=movetime,finish=movetime+160,from=650,to=500,ease=2})
                    if (m>6) then
                        sjudge3[(m-2)%5+1]:DoMoveY({start=movetime,finish=movetime+80,from=-140,to=-190})
                    end
                    if (m>7) then
                        sjudge3[(m-3)%5+1]:DoMoveY({start=movetime,finish=movetime+80,from=-190,to=-240})
                    end
                    if (m>8) then
                        sjudge3[(m-4)%5+1]:DoMoveX({start=movetime,finish=movetime+160,from=500,to=650,ease=1})
                    end
                    sjudge3[(m-1)%5+1]:DoAlpha({start=movetime,finish=movetime,from=0,to=100}) 
                    if (m>8) then
                        sjudge3[(m-4)%5+1]:DoAlpha({start=movetime,finish=movetime+160,from=100,to=0})
                    end
                    table.remove(judgenum,1)
                    m=m+1
                end
            end
        end
    end
end

function OnHit()
     time = Game:Time()
     hitevt = Game:HitEvent()
     judge = hitevt:JudgeResult()
     hitx = hitevt:HitX()


-- Combo
    if (judge ~= 4) then
	combo:DoMoveY({start=time,finish=time+133,from=-10,to=-20,ease=2})
	combotag:DoMoveY({start=time,finish=time+133,from=45,to=35,ease=2})
    end

    for i=1,6 do
        if (hitx==i) then
            for k=1,4 do
                if (judge==k) then
                    table.insert(judgenum,k)
                end
            end
        end
    end

-- Judge
     NoteHP = hitevt:HitX()
    for i=1,6 do
	    if (NoteHP == i) then
	        for k=1,4 do
	            if (judge == k) then
	                sjudge[k]=Module:Shadow(mjudge[k],400)
	                sjudge[k].X=mjudgepos.x[i]
	                sjudge[k].Y=mjudgepos.y[i]
	                
	            
		             sjudge[k]:DoAlpha({start=time,finish=time+200,from=100,to=100,ease=2})
		             sjudge[k]:DoAlpha({start=time+200,finish=time+400,from=100,to=0,ease=2})
		            sjudge[k]:DoResize({start=time,finish=time+10,from=80,to=160,ease=1},{from=50,to=70})
		            sjudge[k]:DoResize({start=time+10,finish=time+150,from=160,to=120,ease=2},{from=70,to=30})
		        end
		    end
		end
	end

-- BothHitline
    if (judge ~= 4) then
        for i=1,6 do
            if (hitx==i) then
                shitx[(cube-1)%5+1]=hitx
                notetime[(cube-1)%5+1]=hitevt:NoteTime()
                    if (notetime[(cube-1)%5+1]==notetime[(cube-2)%5+1] and shitx[(cube-1)%5+1]~=shitx[(cube-2)%5+1]) then
                        hitline[shitx[(cube-2)%5+1]][shitx[(cube-1)%5+1]]:DoAlpha({start=time,finish=time+333,from=100,to=0})
                    end
                cube=cube+1
            end
        end
    end
end