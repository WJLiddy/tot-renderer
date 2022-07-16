using SimpleFileBrowser;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;

public class TRenderer : MonoBehaviour
{
    private readonly float GRIDSIZE = 2.5f;

    public Texture2D minimap;

    // current tick
    private int tick = 0;
    private int maxTick = 0;
    private string levelPrefix = null;

    // time to next tick (always in scale of 1 second)
    private float timeToNextTick = 0;

    private Dictionary<long, Pair<GameObject, HPBar>> pieces;
    private Dictionary<GameObject, Vector3[]> moveTargets = new Dictionary<GameObject, Vector3[]>();
    private ConcurrentQueue<Pair<GameState, Move[]>> states = null; // each element is a pair, i.e. (curr, trans)
    private Thread stateLoaderThread = null;

    public GameObject[] PlayerUIs;
    public Text fileUI;
    public Text tickUI;

    public bool showFOW = false;

    public Slider slider;

    // Start is called before the first frame update
    void Start()
    {
        timeToNextTick = 1f;
    }

    // Update is called once per frame
    void Update()
    {
        if(levelPrefix == null || tick == maxTick)
        {
            return;
        }

        moveAllToTarget();
        timeToNextTick -= Time.deltaTime;
        if (timeToNextTick <= 0)
        {
            moveAllToTarget(true);
            tick++;
            tickUI.text = "TICK " + tick;
            timeToNextTick += 1f;
            beginTransitionAnimationToNextState();
            slider.value = tick;
        }
    }

    void beginTransitionAnimationToNextState()
    {
        if (this.states == null) {
            return;
        }

        // Check to see if next states are ready for processing
        Pair<GameState, Move[]> transitionInfo = null;
        
        if (this.states.IsEmpty || !this.states.TryDequeue(out transitionInfo))
        {
            // Bg thread COULD be working -- it's pretty fast tho
            // if so, wait a bit.
            if (this.stateLoaderThread.IsAlive)
            {
                Thread.Sleep(50);
                //Debug.Log("animating faster than bg thread, halp");
            }
            else
            {
                // nothing to transition to.
            }
        }
        else {
            transitionTo(transitionInfo.First);
            beginPlayingTransitionAnimations(transitionInfo.First, transitionInfo.Second);
        }
    }

    public int MaxHPForType(char t, Player[] players, int team)
    {
        if(team == -2)
        {
            //skeleton
            return 25;
        }

        switch(t)
        {
            case 'v': return 20;

            case 'i':
                switch(players[team].InfLevel)
                {
                    case 1: return 30;
                    case 2: return 60;
                    case 3: return 90;
                }
                break;

            case 'a':
                switch (players[team].ArcLevel)
                {
                    case 1: return 25;
                    case 2: return 35;
                    case 3: return 45;
                }
                break;

            case 'c':
                switch (players[team].CavLevel)
                {
                    case 1: return 45;
                    case 2: return 90;
                    case 3: return 145;
                }
                break;

            case 'h': return 40;
            case 't': return 50;
            case 'r': return 60;
            case 'b': return 60;
            case 's': return 60;
            case 'w': return 80;
            case 'g': return 250;
        }
        //throw error.
        return -1;
    }

    public Vector3 directionForCoord(int x, int y)
    {
        return new Vector3(0, -90 + (Mathf.Rad2Deg * Mathf.Atan2(y,x)), 0);
    }

    public Vector2 getTilePosition(GameObject go)
    {
        return new Vector2(go.transform.position.x / GRIDSIZE, go.transform.position.z / GRIDSIZE);
    }

    // EXCEPTIONALLY slow
    public Dictionary<long, int[]> IDtoCoord(Entity[][] previous)
    {
        Dictionary<long, int[]> coords = new Dictionary<long, int[]>();

        for (int x = 0; x != previous.Length; ++x)
        {
            for (int y = 0; y != previous.Length; ++y)
            {
                if(previous[x][y] != null)
                {
                    coords[previous[x][y].Id] = new int[2] { x, y };
                }
            }
        }
        return coords;
    }


    public void moveAllToTarget(bool immediately = false)
    {
        foreach(var v in moveTargets)
        {
            v.Key.transform.position = Vector3.Lerp(v.Value[1],v.Value[0],timeToNextTick);
            if(immediately)
            {
                v.Key.transform.position = v.Value[1];
            }
        }
    }

    public void spawnArrow(int startX, int startY, int endX, int endY)
    {
        GameObject go = Instantiate(Resources.Load<GameObject>("GamePieces/arrow"));
        go.transform.SetParent(this.transform);
        go.transform.position = new Vector3(startX * -GRIDSIZE, 1f, startY * GRIDSIZE);
        go.transform.forward = new Vector3(-(endX - startX), 0, endY - startY);
        // random arrow constant, 12 u per second seems aight
        go.GetComponent<Rigidbody>().velocity = go.transform.forward * GRIDSIZE * 12;
        var dist = new Vector3(-(endX - startX), 0, endY - startY).magnitude * GRIDSIZE;
        Destroy(go, dist / go.GetComponent<Rigidbody>().velocity.magnitude);

    }
    
    // run all state transitions
    public void beginPlayingTransitionAnimations(GameState previous, Move[] transition)
    {
        moveTargets = new Dictionary<GameObject, Vector3[]>();
        var ids = IDtoCoord(previous.WorldState);
        foreach(var move in transition)
        {
            switch (move.Command)
            {
                // fix or start creation.
                case 'f':
                    pieces[move.Id].First.GetComponent<Animator>().SetTrigger("attack");
                    // find the tile position of the player.
                    var coordStart = ids[move.Id];
                    var coordTarg = ids[(move as TargetMove).Arg];
                    // hacky fix?
                    pieces[move.Id].First.transform.eulerAngles = directionForCoord(coordTarg[0] - coordStart[0], coordTarg[1] - coordStart[1]);
                    break;
                case 'w':
                case 'r':
                case 's':
                case 'h':
                case 'b':
                    pieces[move.Id].First.GetComponent<Animator>().SetTrigger("attack");
                    var coords = (move as XyMove).Arg;
                    pieces[move.Id].First.transform.eulerAngles = directionForCoord(coords[0], coords[1]);
                    break;
                case 'm':
                    var go = pieces[move.Id].First;
                    go.GetComponent<Animator>().SetTrigger("move");
                    var coords2 = (move as XyMove).Arg;
                    go.transform.eulerAngles = directionForCoord(coords2[0], coords2[1]);
                    if (moveTargets.ContainsKey(go))
                    {
                        moveTargets[go][1] += new Vector3(coords2[0] * -GRIDSIZE, 0, coords2[1] * GRIDSIZE);
                    }
                    else
                    {
                        moveTargets[go] = new Vector3[2] { go.transform.position, go.transform.position + new Vector3(coords2[0] * -GRIDSIZE, 0, coords2[1] * GRIDSIZE) };
                    }


                    break;
                case 'k':
                    pieces[move.Id].First.GetComponent<Animator>().SetTrigger("attack");
                    coordStart = ids[move.Id];
                    coordTarg = ids[(move as TargetMove).Arg];

                    var prev = previous.WorldState[coordStart[0]][coordStart[1]];
                    if(prev != null && prev.Type == 'a')
                    {
                        spawnArrow(coordStart[0], coordStart[1], coordTarg[0], coordTarg[1]);
                    }

                    pieces[move.Id].First.transform.eulerAngles = directionForCoord(coordTarg[0] - coordStart[0], coordTarg[1] - coordStart[1]);
                    break;
            }
        }
    }

    // for teams 1-4, return VIL MIL MAX.
    public List<int[]> collectPopStats(Entity[][] state)
    {
        List<int[]> result = new List<int[]>();
        result.Add(new int[3] { 0, 0, 0 });
        result.Add(new int[3] { 0, 0, 0 });
        result.Add(new int[3] { 0, 0, 0 });
        result.Add(new int[3] { 0, 0, 0 });

        for (int x = 0; x != state.Length; ++x)
        {
            for (int y = 0; y != state.Length; ++y)
            {
                var tile = state[x][y];
                if (tile != null && tile.Team >= 0)
                {
                    var team = tile.Team;
                    switch (tile.Type)
                    {
                        case 'v':
                            result[team][0] += 1;
                            break;

                        case 'i':
                        case 'c':
                        case 'a':
                            result[team][1] += 1;
                            break;

                        case 'w':
                        case 'h':
                            result[team][2] += 1;
                            break;
                    }
                }
            }
        }
        return result;
    }

    public int getGameEnd()
    {
        int tmaxTick = 0;
        while(File.Exists(levelPrefix + tmaxTick + ".json"))
        {
            tmaxTick++;
        }
        // last state won't have another transition state so subtract 2.
        return tmaxTick - 2;
    }

    public void writePlayerDataToCanvas(Entity[][] world_state, Player[] players)
    {
        var popStats = collectPopStats(world_state);
        for(int i = 0; i != players.Length; ++i)
        {
            PlayerUIs[i].transform.Find("Name").GetComponent<Text>().text = players[i].Name;
            PlayerUIs[i].transform.Find("Gold").GetComponent<Text>().text = players[i].Gold + "";
            PlayerUIs[i].transform.Find("Wood").GetComponent<Text>().text = players[i].Wood + "";
            PlayerUIs[i].transform.Find("InfLev").GetComponent<Text>().text = players[i].InfLevel + "";
            PlayerUIs[i].transform.Find("ArcLev").GetComponent<Text>().text = players[i].ArcLevel + "";
            PlayerUIs[i].transform.Find("CavLev").GetComponent<Text>().text = players[i].CavLevel + "";
            PlayerUIs[i].transform.Find("Pop").GetComponent<Text>().text = "V" + popStats[i][0] + "/M" + popStats[i][1] + "/" + popStats[i][2];
        }
    }

    //spaws things and deletes dead.
    public void transitionTo(GameState nextState)
    {
        // see if any exist.
        HashSet<long> seenIDs = new HashSet<long>();

        var ws = nextState.WorldState;

        for (int x = 0; x < ws.Length; ++x)
        {
            for (int y = 0; y < ws.Length; ++y)
            {
                var node = ws[x][y];
                if (node == null)
                    continue;

                // force update
                var unitNode = node as Unit;
                var buildingNode = node as Building;
                pieces.TryGetValue(node.Id, out var selectedPiecePair);
                if(buildingNode != null && buildingNode.Constructed && selectedPiecePair.First.name == "CONSTRUCTION")
                {
                    var newPiece = loadPieceOfType(x, y, node.Type, node.Team, buildingNode.Constructed, nextState.Players);
                    var bar = newPiece.GetComponentInChildren<HPBar>();
                    selectedPiecePair = new Pair<GameObject, HPBar>(newPiece, bar);
                    pieces[node.Id] = selectedPiecePair;
                }


                if (!pieces.ContainsKey(node.Id))
                {
                    int team = node.Team;
                    var newPiece = loadPieceOfType(x, y, node.Type, node.Team, buildingNode?.Constructed ?? false, nextState.Players);
                    var bar = newPiece.GetComponentInChildren<HPBar>();
                    selectedPiecePair = new Pair<GameObject, HPBar>(newPiece, bar);
                    pieces[node.Id] = selectedPiecePair;
                }

                if(node != null)
                {
                    long nid = node.Id;
                    seenIDs.Add(nid);

                    // reload pieces in case rank changes.
                    var selectedPieceName = selectedPiecePair.First.name;
                    var firstchar = selectedPieceName[0];
                    int level = selectedPieceName[1] - 48; // '0' is 48 in decimal ASCII. so char '1' is 49 and gives us decimal 1.
                    if (firstchar == 'i' || firstchar == 'a' || firstchar == 'c')
                    {
                        int team = node.Team;
                        // skeles never upgrade.
                        if(team == -2)
                        {
                            return;
                        }
                        int inflev = nextState.Players[team].InfLevel;
                        int arclev = nextState.Players[team].ArcLevel;
                        int cavlev = nextState.Players[team].CavLevel;

                        if (
                            (firstchar == 'i' && level != inflev) ||
                            (firstchar == 'a' && level != arclev) ||
                            (firstchar == 'c' && level != cavlev)
                        )
                        {
                            Destroy(selectedPiecePair.First);
                            var newPiece = loadPieceOfType(x, y, node.Type, node.Team, buildingNode?.Constructed ?? false, nextState.Players);
                            var bar = newPiece.GetComponentInChildren<HPBar>();
                            selectedPiecePair = new Pair<GameObject, HPBar>(newPiece, bar);
                            pieces[nid] = selectedPiecePair;
                        }
                    }
                    selectedPiecePair.Second.setHPBar(node.Hp, MaxHPForType(node.Type, nextState.Players, node.Team));
                }
            }
        }

        List<long> killList = new List<long>();
        // if anything dissapeared, delete.
        foreach(var v in pieces)
        {
            if(!seenIDs.Contains(v.Key))
            {
                // this died
                killList.Add(v.Key);
            }
        }

        foreach(var k in killList)
        {
            // later - play animation
            Destroy(pieces[k].First);
            pieces.Remove(k);
        }

        updateMiniMap(nextState.WorldState);
        writePlayerDataToCanvas(nextState.WorldState, nextState.Players);
    }

    public void Clear()
    {
        foreach (Transform child in transform)
        {
            GameObject.Destroy(child.gameObject);
        }
        moveTargets = new Dictionary<GameObject, Vector3[]>();
    }

    public void recolorUnit(GameObject unit, int teamID)
    {
        foreach(var v in unit.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            v.material = Resources.Load<Material>("GamePieces/" + teamID + "i");
        }

        foreach (var v in unit.GetComponentsInChildren<MeshRenderer>())
        {
            v.material = Resources.Load<Material>("GamePieces/" + teamID + "i");
        }
    }

    public void recolorBuilding(GameObject unit, int teamID)
    {
        foreach (var v in unit.GetComponentsInChildren<MeshRenderer>())
        {
            v.material = Resources.Load<Material>("GamePieces/" + teamID + "b");
        }
    }

    // force jump to this gamestate, clearing up the board.
    public void fromJSON(GameState scene)
    {
        Clear();
        pieces = new Dictionary<long, Pair<GameObject, HPBar>>();

        for (int x = -1; x <= scene.WorldState.Length; ++x)
        {
            for (int y = -1; y <= scene.WorldState.Length; ++y)
            {
                // barrier
                if (x == -1 || y == -1 || y == scene.WorldState.Length || x == scene.WorldState.Length)
                {
                    GameObject go = Instantiate(Resources.Load<GameObject>("GamePieces/wall"));
                    go.transform.SetParent(this.transform);
                    go.transform.position = new Vector3(x * -GRIDSIZE, 0, y * GRIDSIZE);
                }
                else
                {
                    placeNewPiece(scene, x, y);
                }
            }
        }
        updateMiniMap(scene.WorldState);
        writePlayerDataToCanvas(scene.WorldState, scene.Players);
    }

    public Color colorForTeam(int team)
    {
        switch(team)
        {
            case 0: return Color.red;
            case 1: return Color.blue;
            case 2: return Color.magenta;
            case 3: return Color.cyan;
        }
        return Color.black;
    }

    // TODO: is -1 legal?
    public long idAt(int x, int y, SimpleJSON.JSONNode scene)
    {
        if (x < 0 || y < 0 || scene.AsArray.Count <= x || scene.AsArray.Count <= y)
        {
            return -1;
        }

        if(scene[x][y] == null)
        {
            return -1;
        }
        
        return scene[x][y]["id"];
    }


    public void updateMiniMap(Entity[][] state)
    {
        for(int x = 0; x != state.Length; ++x)
        {
            for (int y = 0; y != state.Length; ++y)
            {              
                minimap.SetPixel(x, y, Color.gray);
                if (state[x][y] != null)
                {
                    switch (state[x][y].Type)
                    {
                        case 't':
                            minimap.SetPixel(x, y, Color.green);
                            break;
                        case 'g':
                            minimap.SetPixel(x, y, Color.yellow);
                            break;
                        default:
                            minimap.SetPixel(x, y, colorForTeam(state[x][y].Team));
                            break;

                    }
                }
            }
        }
        minimap.Apply();
    }
 
    public GameObject loadPieceOfType(int x, int y, char value, int team, bool constructed, Player[] teams)
    {
        GameObject go = null;

        switch (value)
        {
            case 't':
                go = Instantiate(Resources.Load<GameObject>("GamePieces/tree"));
                break;
            case 'g':
                go = Instantiate(Resources.Load<GameObject>("GamePieces/gold"));
                break;
            case 'v':
                go = Instantiate(Resources.Load<GameObject>("GamePieces/vil"));
                recolorUnit(go, team);
                break;
     
                // doens't hndle level
            case 'i':
                int inflev = teams[team].InfLevel;
                go = Instantiate(Resources.Load<GameObject>("GamePieces/inf" + inflev));
                go.name = "i" + inflev;
                recolorUnit(go, team);
                break;
            case 'a':
                int arclev = 1;
                if (team != -2)
                {
                    arclev = teams[team].ArcLevel;
                    go = Instantiate(Resources.Load<GameObject>("GamePieces/arc" + arclev));
                    go.name = "a" + arclev;
                    recolorUnit(go, team);
                } else
                {
                    go = Instantiate(Resources.Load<GameObject>("GamePieces/skel"));
                }

                break;
            case 'c':
                int cavlev = teams[team].CavLevel;
                go = Instantiate(Resources.Load<GameObject>("GamePieces/cav" + cavlev));
                go.name = "c" + cavlev;
                recolorUnit(go, team);
                break;

            case 'w':
                if (constructed)
                {
                    go = Instantiate(Resources.Load<GameObject>("GamePieces/tow"));
                } else
                {
                    go = Instantiate(Resources.Load<GameObject>("GamePieces/towc"));
                    go.name = "CONSTRUCTION";
                }
                recolorBuilding(go, team);
                break;

            case 'h':
                if (constructed)
                {
                    go = Instantiate(Resources.Load<GameObject>("GamePieces/hou"));
                }
                else
                {
                    go = Instantiate(Resources.Load<GameObject>("GamePieces/houc"));
                    go.name = "CONSTRUCTION";
                }
                recolorBuilding(go, team);
                break;

            case 'b':
                if (constructed)
                {
                    go = Instantiate(Resources.Load<GameObject>("GamePieces/bar"));
                }
                else
                {
                    go = Instantiate(Resources.Load<GameObject>("GamePieces/barc"));
                    go.name = "CONSTRUCTION";
                }
                recolorBuilding(go, team);
                break;

            case 'r':
                if (constructed)
                {
                    go = Instantiate(Resources.Load<GameObject>("GamePieces/ran"));
                }
                else
                {
                    go = Instantiate(Resources.Load<GameObject>("GamePieces/ranc"));
                    go.name = "CONSTRUCTION";
                }
                recolorBuilding(go, team);
                break;

            case 's':
                if (constructed)
                {
                    go = Instantiate(Resources.Load<GameObject>("GamePieces/sta"));
                }
                else
                {
                    go = Instantiate(Resources.Load<GameObject>("GamePieces/stac"));
                    go.name = "CONSTRUCTION";
                }
                recolorBuilding(go, team);
                break;
        }
        
        if(go == null)
        {
            return go;
        }

        go.transform.SetParent(this.transform);
        go.transform.position = new Vector3(x * -GRIDSIZE, 0, y * GRIDSIZE);

        var hp = Instantiate(Resources.Load<GameObject>("GamePieces/HPBar"));
        hp.transform.SetParent(go.transform);
        hp.transform.position = new Vector3(go.transform.position.x, 2f, go.transform.position.z);
        return go;
    }

    public void placeNewPiece(GameState scene, int x, int y)
    {
        var node = scene.WorldState[x][y];
        if (node != null)
        {
            if (pieces.ContainsKey(node.Id))
            {
                // already loaded this (it's part of a building)
                return;
            }
            
            var go = loadPieceOfType(x, y, node.Type, node.Team, (node as Building)?.Constructed ?? false, scene.Players);
            var bar = go.GetComponentInChildren<HPBar>();
            pieces.Add(node.Id, new Pair<GameObject, HPBar>(go, bar));
        }
    }

    public void onLoadSelected()
    { 
        // Coroutine example
        StartCoroutine(ShowLoadDialogCoroutine() );
    }

    IEnumerator ShowLoadDialogCoroutine()
    {
        // Show a load file dialog and wait for a response from user
        // Load file/folder: both, Allow multiple selection: true
        // Initial path: default (Documents), Initial filename: empty
        // Title: "Load File", Submit button text: "Load"
        yield return FileBrowser.WaitForLoadDialog(FileBrowser.PickMode.Folders, false, Path.GetFullPath(Path.Combine(Application.dataPath, @"..\")), null, "Load Files and Folders", "Load");

        // Dialog is closed
        // Print whether the user has selected some files/folders or cancelled the operation (FileBrowser.Success)

        fileUI.text = Path.GetFileName(FileBrowser.Result[0]);
        if (FileBrowser.Success)
        {
            levelPrefix = FileBrowser.Result[0] + "\\";
            maxTick = getGameEnd();
            tick = 0;
            timeToNextTick = 1f;
            slider.maxValue = maxTick;
            slider.value = 0;
            var parsed = SimpleJSON.JSON.Parse(File.ReadAllText(FileBrowser.Result[0]+"\\0.json"));
            fromJSON(ToGameState(parsed));
            ResetStateLoading();
            beginTransitionAnimationToNextState();
        }
    }

    public void onPlaybackPositionChanged(System.Single pos)
    {
        var newtick = (int)(pos);
        if (newtick != tick)
        {
            // call this first - stop illegal moves.
            Clear();
            Time.timeScale = 0f;
            timeToNextTick = 1f;
            tick = newtick;
            tickUI.text = "TICK " + tick;
            var parsed = SimpleJSON.JSON.Parse(File.ReadAllText(FileBrowser.Result[0] + "\\" + tick + ".json"));
            fromJSON(ToGameState(parsed));
            ResetStateLoading();
            beginTransitionAnimationToNextState();
        }
    }

    private void ResetStateLoading()
    {
        // kick off loading states; reset existing loading if needed
        if (this.stateLoaderThread != null && this.stateLoaderThread.IsAlive)
        {
            try
            {
                this.stateLoaderThread.Abort();
                this.stateLoaderThread.Join(3000); // might need to wait a bit for bg thread to wrap up
            }
            catch (Exception)
            {
                // whatever
            }
        }

        this.states = new ConcurrentQueue<Pair<GameState, Move[]>>();
        this.stateLoaderThread = new Thread(new ThreadStart(CreateLoadStates(this.levelPrefix, this.maxTick, this.tick + 1)));
        this.stateLoaderThread.Start();
    }

    Action CreateLoadStates(string levelPrefix, int maxTick, int startTick)
    {
        var states = this.states;
        return delegate
        {
            // Load all states and fill up the queue for the renderer to process.
            // Some considerations:
            // 1) Don't go too fast (too many states could fill up memory)
            // 2) quit once we are at the max tick
            // 3) (TODO) In the Unity editor, when the game is played and closed, this thread will keep going. Dunno how to fix this
            // 4) (TODO) Bug: If animating outpaces the bg thread, then the tick will increment in the UI while none of the units do
            //    anything. So tick 77 in the json files might only happen at tick 151 or something in the viewed game. Does not seem
            //    to be much of a problem when the game is compiled and run outside of Unity, especially when animating the units takes
            //    time (i.e. maybe we have dozens of units on-screen).
            var currentTick = startTick;
            var maxStates = 20;
            while (currentTick < maxTick)
            {
                if (states.Count >= maxStates)
                {
                    Thread.Sleep(10); // wait to see if main thread processes some states
                    //Debug.Log("bg thread 2 fast, halp");
                }
                else
                {
                    var curr = SimpleJSON.JSON.Parse(System.IO.File.ReadAllText(levelPrefix + currentTick + ".json"));
                    var trans = SimpleJSON.JSON.Parse(System.IO.File.ReadAllText(levelPrefix + currentTick + "move.json"));
                    states.Enqueue(new Pair<GameState, Move[]>(ToGameState(curr), ToMoves(trans)));
                    currentTick++;
                }
            }
        };
    }

    private static Move[] ToMoves(SimpleJSON.JSONNode trans)
    {
        var result = new Move[trans.Count];
        var index = 0;
        foreach (var node in trans.Children) {
            var id = node["id"].AsLong;
            var command = node["command"].Value[0];
            var arg = node["arg"];
            Move move;
            try {
                long target = long.Parse(arg.Value);
                move = new TargetMove()
                {
                    Id = id,
                    Command = command,
                    Arg = target
                };
            }
            catch (Exception)
            {
                // whoops it's an Xy
                int[] xy = new int[] {arg[0].AsInt, arg[1].AsInt};
                move = new XyMove()
                {
                    Id = id,
                    Command = command,
                    Arg = xy
                };
            }

            result[index] = move;
            index++;
        }

        return result;
    }

    private static GameState ToGameState(SimpleJSON.JSONNode jsonState)
    {
        // players
        var playersNode = jsonState["players"];
        var players = new Player[playersNode.Count];
        var index = 0;
        foreach (var node in playersNode.Children)
        {
            var player = new Player() {
                CavLevel = node["cav_level"].AsInt,
                InfLevel = node["inf_level"].AsInt,
                ArcLevel = node["arc_level"].AsInt,
                Gold = node["gold"].AsInt,
                Wood = node["wood"].AsInt,
                Name = node["name"].Value
            };

            players[index] = player;
            index++;
        }

        // world state
        var worldNode = jsonState["world_state"];
        var size = worldNode.Count;
        Entity[][] worldState = new Entity[size][];
        for (var x = 0; x < size; x++) {
            worldState[x] = new Entity[size];
            for (var y = 0; y < size; y++) {
                var entity = worldNode[x][y];
                if (entity.Tag != SimpleJSON.JSONNodeType.NullValue) {
                    Entity newEntity;
                    var id = entity["id"].AsLong;
                    var type = entity["type"].Value[0];
                    var hp = entity["hp"].AsInt;
                    var team = entity["team"].AsInt;

                    if (entity.HasKey("constructed")) {
                        newEntity = new Building() {
                            Id = id,
                            Type = type,
                            Hp = hp,
                            Team = team,
                            Constructed = entity["constructed"].AsBool,
                            TrainTime = entity["traintime"].AsInt
                        };
                    }
                    else {
                        newEntity = new Unit() {
                            Id = id,
                            Type = type,
                            Hp = hp,
                            Team = team
                        };
                    }

                    worldState[x][y] = newEntity;
                }
            }
        }

        return new GameState() {
            WorldState = worldState,
            Players = players
        };
    }
}

public abstract class Move
{
	public long Id {get; set;}
	public char Command {get;set;}
	public int? Team {get; set;}
}

public class TargetMove : Move
{
	public long Arg {get;set;}
}

public class XyMove : Move
{
	public int[] Arg {get;set;}
}

public class Pair<S, T> {
    public S First {get; set;}
    public T Second {get; set;}

    public Pair(S first, T second) {
        First = first;
        Second = second;
    }
}

public abstract class Entity {
    public long Id {get; set;}
    public char Type {get; set;}
    public int Hp {get; set;}
    public int Team {get; set;}
}

public class Unit : Entity {}

public class Building : Entity {
    public bool Constructed {get; set;}
    public int TrainTime {get; set;}
}

public class Player {
    public int CavLevel {get; set;}
    public int InfLevel {get; set;}
    public int ArcLevel {get; set;}
    public int Gold {get; set;}
    public int Wood {get; set;}
    public string Name {get; set;}
}

public class GameState {
    public Entity[][] WorldState {get; set;}
    public Player[] Players {get; set;}
}
