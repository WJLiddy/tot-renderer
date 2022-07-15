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

    private Dictionary<long, GameObject> pieces;
    private Dictionary<GameObject, Vector3[]> moveTargets = new Dictionary<GameObject, Vector3[]>();
    private ConcurrentQueue<SimpleJSON.JSONNode[]> states = null; // each element is a pair, i.e. (curr, trans)
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
        SimpleJSON.JSONNode[] transitionInfo = null;
        
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
                // nothing to transition to
            }
        }
        else {
            transitionTo(transitionInfo[0]);
            beginPlayingTransitionAnimations(transitionInfo[0], transitionInfo[1]);
        }
    }

    public int MaxHPForType(string t, SimpleJSON.JSONNode players, int team)
    {
        if(team == -2)
        {
            //skeleton
            return 25;
        }

        switch(t)
        {
            case "v": return 20;

            case "i":
                switch(players[team]["inf_level"].AsInt)
                {
                    case 1: return 30;
                    case 2: return 60;
                    case 3: return 90;
                }
                break;

            case "a":
                switch (players[team]["arc_level"].AsInt)
                {
                    case 1: return 25;
                    case 2: return 35;
                    case 3: return 45;
                }
                break;

            case "c":
                switch (players[team]["cav_level"].AsInt)
                {
                    case 1: return 45;
                    case 2: return 90;
                    case 3: return 145;
                }
                break;

            case "h": return 40;
            case "t": return 50;
            case "r": return 60;
            case "b": return 60;
            case "s": return 60;
            case "w": return 80;
            case "g": return 250;
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
    public Dictionary<long, int[]> IDtoCoord(SimpleJSON.JSONNode previous)
    {
        Dictionary<long, int[]> coords = new Dictionary<long, int[]>();

        for (int x = 0; x != previous.AsArray.Count; ++x)
        {
            for (int y = 0; y != previous.AsArray.Count; ++y)
            {
                if(previous[x][y] != null)
                {
                    coords[previous[x][y]["id"].AsLong] = new int[2] { x, y };
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
    public void beginPlayingTransitionAnimations(SimpleJSON.JSONNode previous, SimpleJSON.JSONNode transition)
    {
        moveTargets = new Dictionary<GameObject, Vector3[]>();
        var ids = IDtoCoord(previous["world_state"]);
        foreach(var moveset in transition.AsArray)
        {
            var move = moveset.Value;

            switch (move["command"].Value)
            {
                // fix or start creation.
                case "f":
                    pieces[move["id"].AsLong].GetComponent<Animator>().SetTrigger("attack");
                    // find the tile position of the player.
                    var coordStart = ids[move["id"].AsLong];
                    var coordTarg = ids[move["arg"].AsLong];
                    // hacky fix?
                    pieces[move["id"].AsLong].transform.eulerAngles = directionForCoord(coordTarg[0] - coordStart[0], coordTarg[1] - coordStart[1]);
                    break;
                case "w":
                case "r":
                case "s":
                case "h":
                case "b":
                    pieces[move["id"].AsLong].GetComponent<Animator>().SetTrigger("attack");
                    pieces[move["id"].AsLong].transform.eulerAngles = directionForCoord(move["arg"][0], move["arg"][1]);
                    break;
                case "m":
                    var go = pieces[move["id"].AsLong];
                    go.GetComponent<Animator>().SetTrigger("move");
                    go.transform.eulerAngles = directionForCoord(move["arg"][0], move["arg"][1]);
                    if (moveTargets.ContainsKey(go))
                    {
                        moveTargets[go][1] += new Vector3(move["arg"][0] * -GRIDSIZE, 0, move["arg"][1] * GRIDSIZE);
                    }
                    else
                    {
                        moveTargets[go] = new Vector3[2] { go.transform.position, go.transform.position + new Vector3(move["arg"][0] * -GRIDSIZE, 0, move["arg"][1] * GRIDSIZE) };
                    }


                    break;
                case "k":
                    pieces[move["id"].AsLong].GetComponent<Animator>().SetTrigger("attack");
                    coordStart = ids[move["id"].AsLong];
                    coordTarg = ids[move["arg"].AsLong];

                    if(previous["world_state"][coordStart[0]][coordStart[1]]["type"] == "a")
                    {
                        spawnArrow(coordStart[0], coordStart[1], coordTarg[0], coordTarg[1]);
                    }

                    pieces[move["id"].AsLong].transform.eulerAngles = directionForCoord(coordTarg[0] - coordStart[0], coordTarg[1] - coordStart[1]);
                    break;
            }
        }
    }

    // for teams 1-4, return VIL MIL MAX.
    public List<int[]> collectPopStats(SimpleJSON.JSONNode state)
    {
        List<int[]> result = new List<int[]>();
        result.Add(new int[3] { 0, 0, 0 });
        result.Add(new int[3] { 0, 0, 0 });
        result.Add(new int[3] { 0, 0, 0 });
        result.Add(new int[3] { 0, 0, 0 });

        for (int x = 0; x != state.AsArray.Count; ++x)
        {
            for (int y = 0; y != state.AsArray.Count; ++y)
            {
                var tile = state[x][y];
                if (tile != null && tile["team"] >= 0)
                {
                    var team = tile["team"];
                    switch (tile["type"].Value)
                    {
                        case "v":
                            result[team][0] += 1;
                            break;

                        case "i":
                        case "c":
                        case "a":
                            result[team][1] += 1;
                            break;

                        case "w":
                        case "h":
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

    public void writePlayerDataToCanvas(SimpleJSON.JSONArray world_state, SimpleJSON.JSONArray players)
    {
        var popStats = collectPopStats(world_state);
        for(int i = 0; i != players.Count; ++i)
        {
            PlayerUIs[i].transform.Find("Name").GetComponent<Text>().text = players[i]["name"];
            PlayerUIs[i].transform.Find("Gold").GetComponent<Text>().text = players[i]["gold"];
            PlayerUIs[i].transform.Find("Wood").GetComponent<Text>().text = players[i]["wood"];
            PlayerUIs[i].transform.Find("InfLev").GetComponent<Text>().text = players[i]["inf_level"];
            PlayerUIs[i].transform.Find("ArcLev").GetComponent<Text>().text = players[i]["arc_level"];
            PlayerUIs[i].transform.Find("CavLev").GetComponent<Text>().text = players[i]["cav_level"];
            PlayerUIs[i].transform.Find("Pop").GetComponent<Text>().text = "V" + popStats[i][0] + "/M" + popStats[i][1] + "/" + popStats[i][2];
        }
    }

    //spaws things and deletes dead.
    public void transitionTo(SimpleJSON.JSONNode nextState)
    {
        List<string> piecenames = new List<string>() { "i1", "i2", "i3", "a1", "a2", "a3", "c1", "c2", "c3" };

        // see if any exist.
        HashSet<long> seenIDs = new HashSet<long>();

        var ws = nextState["world_state"];

        for (int x = 0; x < ws.AsArray.Count; ++x)
        {
            for (int y = 0; y < ws.AsArray.Count; ++y)
            {
                var node = ws[x][y];

                // force update
                if(node != null && node["constructed"].AsBool && pieces[node["id"].AsLong].name == "CONSTRUCTION")
                {
                    SimpleJSON.JSONNode player = nextState[node["team"].AsInt];
                    pieces[node["id"].AsLong] = loadPieceOfType(x, y, node["type"], node["team"], node["constructed"], nextState["players"].AsArray);
                }


                if (node != null && !pieces.ContainsKey(node["id"].AsLong))
                {
                    int team = node["team"];
                    SimpleJSON.JSONNode player = nextState[node["team"].AsInt];
                    pieces[node["id"].AsLong] = loadPieceOfType(x, y, node["type"], node["team"], node["constructed"], nextState["players"].AsArray);
                }

                if(node != null)
                {
                    long nid = node["id"].AsLong;
                    seenIDs.Add(nid);

                    // reload pieces in case rank changes.
                    if (piecenames.Contains(pieces[node["id"].AsLong].name))
                    {
                        int team = node["team"];
                        // skeles never upgrade.
                        if(team == -2)
                        {
                            return;
                        }
                        int inflev = nextState["players"][team]["inf_level"];
                        int arclev = nextState["players"][team]["arc_level"];
                        int cavlev = nextState["players"][team]["cav_level"];

                        if (
                            (pieces[nid].name.ToCharArray()[0] == 'i' && pieces[nid].name != ("i" + inflev)) ||
                            (pieces[nid].name.ToCharArray()[0] == 'a' && pieces[nid].name != ("a" + arclev)) ||
                            (pieces[nid].name.ToCharArray()[0] == 'c' && pieces[nid].name != ("c" + cavlev))
                            )
                        {
                            Destroy(pieces[nid]);
                            pieces[nid] = loadPieceOfType(x, y, node["type"], node["team"], node["constructed"], nextState["players"].AsArray);
                        }
                    }
                    pieces[nid].GetComponentInChildren<HPBar>().setHPBar(node["hp"], MaxHPForType(node["type"], nextState["players"].AsArray, node["team"].AsInt));
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
            Destroy(pieces[k]);
            pieces.Remove(k);
        }

        updateMiniMap(nextState["world_state"]);
        writePlayerDataToCanvas(nextState["world_state"].AsArray,nextState["players"].AsArray);
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
    public void fromJSON(SimpleJSON.JSONNode scene)
    {
        Clear();
        pieces = new Dictionary<long, GameObject>();

        for (int x = -1; x <= scene["world_state"].AsArray.Count; ++x)
        {
            for (int y = -1; y <= scene["world_state"].AsArray.Count; ++y)
            {
                // barrier
                if (x == -1 || y == -1 || y == scene["world_state"].AsArray.Count || x == scene["world_state"].AsArray.Count)
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
        updateMiniMap(scene["world_state"]);
        writePlayerDataToCanvas(scene["world_state"].AsArray, scene["players"].AsArray);
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


    public void updateMiniMap(SimpleJSON.JSONNode state)
    {
        for(int x = 0; x != state.AsArray.Count; ++x)
        {
            for (int y = 0; y != state.AsArray.Count; ++y)
            {              
                minimap.SetPixel(x, y, Color.gray);
                if (state[x][y] != null)
                {
                    switch (state[x][y]["type"].Value)
                    {
                        case "t":
                            minimap.SetPixel(x, y, Color.green);
                            break;
                        case "g":
                            minimap.SetPixel(x, y, Color.yellow);
                            break;
                        default:
                            minimap.SetPixel(x, y, colorForTeam(state[x][y]["team"]));
                            break;

                    }
                }
            }
        }
        minimap.Apply();
    }
 
    public GameObject loadPieceOfType(int x, int y, string value, int team, bool constructed, SimpleJSON.JSONArray teams)
    {
        GameObject go = null;

        switch (value)
        {
            case "t":
                go = Instantiate(Resources.Load<GameObject>("GamePieces/tree"));
                break;
            case "g":
                go = Instantiate(Resources.Load<GameObject>("GamePieces/gold"));
                break;
            case "v":
                go = Instantiate(Resources.Load<GameObject>("GamePieces/vil"));
                recolorUnit(go, team);
                break;
     
                // doens't hndle level
            case "i":
                int inflev = teams[team]["inf_level"];
                go = Instantiate(Resources.Load<GameObject>("GamePieces/inf" + inflev));
                go.name = "i" + inflev;
                recolorUnit(go, team);
                break;
            case "a":
                int arclev = 1;
                if (team != -2)
                {
                    arclev = teams[team]["arc_level"];
                    go = Instantiate(Resources.Load<GameObject>("GamePieces/arc" + arclev));
                    go.name = "a" + arclev;
                    recolorUnit(go, team);
                } else
                {
                    go = Instantiate(Resources.Load<GameObject>("GamePieces/skel"));
                }

                break;
            case "c":
                int cavlev = teams[team]["cav_level"];
                go = Instantiate(Resources.Load<GameObject>("GamePieces/cav" + cavlev));
                go.name = "c" + cavlev;
                recolorUnit(go, team);
                break;

            case "w":
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

            case "h":
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

            case "b":
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

            case "r":
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

            case "s":
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

    public void placeNewPiece(SimpleJSON.JSONNode scene, int x, int y)
    {
        var node = scene["world_state"][x][y];
        if (node != null)
        {
            if (pieces.ContainsKey(node["id"].AsLong))
            {
                // already loaded this (it's part of a building)
                return;
            }
            
            var go = loadPieceOfType(x, y, node["type"], node["team"], node["constructed"], scene["players"].AsArray);
            pieces.Add(node["id"].AsLong, go);
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
            fromJSON(SimpleJSON.JSON.Parse(File.ReadAllText(FileBrowser.Result[0]+"\\0.json")));

            // kick off loading states for the new game; reset existing loading if needed
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

            this.states = new ConcurrentQueue<SimpleJSON.JSONNode[]>();
            this.stateLoaderThread = new Thread(new ThreadStart(CreateLoadStates(this.levelPrefix, this.maxTick)));
            this.stateLoaderThread.Start();
            
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
            fromJSON(SimpleJSON.JSON.Parse(File.ReadAllText(FileBrowser.Result[0] + "\\" + tick + ".json")));
            beginTransitionAnimationToNextState();
        }
    }

    Action CreateLoadStates(string levelPrefix, int maxTick)
    {
        var states = this.states;
        return delegate
        {
            // Load all states and fill up the queue for the renderer to process.
            // Some considerations:
            // 1) Don't go too fast (too many states could fill up memory)
            // 2) quit once we are at the max tick
            // 3) (TODO) In the Unity editor, when the game is played and closed, this thread will keep going. Dunno how to fix this
            var currentTick = 0;
            var maxStates = 15;
            while (currentTick < maxTick)
            {
                if (states.Count >= maxStates)
                {
                    Thread.Sleep(100); // wait to see if main thread processes some states
                    //Debug.Log("bg thread 2 fast, halp");
                }
                else
                {
                    var curr = SimpleJSON.JSON.Parse(System.IO.File.ReadAllText(levelPrefix + currentTick + ".json"));
                    var trans = SimpleJSON.JSON.Parse(System.IO.File.ReadAllText(levelPrefix + currentTick + "move.json"));
                    states.Enqueue(new SimpleJSON.JSONNode[]{curr, trans});
                    currentTick++;
                }
            }
        };
    }
}
