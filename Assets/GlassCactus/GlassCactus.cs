using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using Random = UnityEngine.Random;
using System.Text.RegularExpressions;
using System.Security.Policy;

public class GlassCactus : MonoBehaviour {

    // Bomb Info Reference
    public KMAudio audioManager;
    public KMColorblindMode colorblindManager;
    bool isColorblindModeActive;
    public KMRuleSeedable ruleseedManager;
    public KMBombModule thisModule;
    public KMSelectable moduleSelectable;



    // Puzzle Data
    enum SpikeColour { Red, Green, Blue, Yellow };
    enum ColourOccurences { Least, SecondLeast, SecondMost, Most };
    /// <summary> Order is: The four RGBY columns, 3 per, in clockwise order outer-to-inner (not including the outermost);
    /// and then the 3 leftover half-circle rows, starting at East going clockwise. </summary>
    List<SpikeColour> manualDiagram = new List<SpikeColour>();
    /// <summary> The 32 Spikes on the Cactus, 4 rows of 8, starting at the Outer North, going clockwise then inner. </summary>
    SpikeColour[] cactusGrid = new SpikeColour[32];

    /// <summary> The 4 Colours, in order of least present to most present, for Puzzle generation and for Souvenir </summary>
    Dictionary<ColourOccurences, SpikeColour> colourOccurenceHierarchy;
    /// <summary> How much of each Spike Colour is present on the Cactus </summary>
    Dictionary<SpikeColour, int> numberOfSpikesPerColour = new Dictionary<SpikeColour, int>();
    ColourOccurences firstColourOccurenceRule;
    ColourOccurences secondColourOccurenceRule;

    enum BreezeMovements { Up, Down, Left, Right, UpLeft, UpRight, DownLeft, DownRight };
    enum BreezeRotation { Clockwise, CounterClockwise };
    List<BreezeMovements> breezeMovementSequence = new List<BreezeMovements>();
    BreezeRotation selectedBreezeRotation;

    List<SpikeColour> finalSequenceColours = new List<SpikeColour>();

    List<int> expectedNextSpikePressed;
    int currentExpectedIndexInSequence;




    // Visual Data
    public GameObject SpikePrefab;
    KMSelectable[] spawnedSpikes;
    /// <summary> Red Green Blue Yellow </summary>
    public Color[] spikeColours;
    public MeshRenderer cactusMeshRenderer;


    public Transform breezeParticleSystem;
    float BreezeRotationConstant1, BreezeRotationConstant2;
    enum VisualBreezeMovementState { None, Moving, Spinning, PreparingToMove };
    VisualBreezeMovementState currentBreezeMovementState;
    float currentBreezeMovementTimer;
    int currentBreezeMovementIndexShown;
    Vector3[] BreezeMovementLocalPositions;
    Vector3 currentBreezeMovementTarget;
    const float startingBreezeHeight = 0.035f;

    public AudioClip SolveAudio;
    public AudioClip[] SpikeRemovalAudio;


    // Logging Data
    static int moduleIdCounter = 1;
    int moduleId;
    bool moduleSolved;


    // =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
    //    Vanilla
    // =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=

#pragma warning disable IDE0051 // Remove unused private members
    void Awake()
    {
        // Randomize Cactus Colour
        cactusMeshRenderer.material.SetColor("_Color", Color.HSVToRGB(Random.value, Random.Range(0.4f, 0.9f), Random.Range(0.6f, 1f)));

        BreezeRotationConstant1 = Random.Range(0.5f, 2f);
        BreezeRotationConstant2 = 2.5f - BreezeRotationConstant1;

        currentBreezeMovementState = VisualBreezeMovementState.None;
    }

    // Use this for initialization
    void Start()
    {
        moduleId = moduleIdCounter++;

        GeneratePuzzle();
        SpawnAndManageSpikes();
        DetermineValidFirstPress();
        PrepareVisualBreezeMovementData();
    }
   
    void Update()
    {
        UpdateBreezePosition();
    }
#pragma warning restore IDE0051 // Remove unused private members

    // =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
    //    Player Inputs
    // =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=

    void PressedCactusSpike(int spikeIndex, KMSelectable spikeSelectable)
    {
        // You shouldn't be able to press any Spike afterwards
        if (moduleSolved)
        { return; }

        // Only feedback on the successful
        if (expectedNextSpikePressed.Contains(spikeIndex))
        {
            // Remove the Spike
            StartCoroutine(RemoveSingleSpikeCoroutine(spikeSelectable.transform));
            PlaySoundAndForget(SpikeRemovalAudio.PickRandom());

            // Very small Interaction Punch as it's still a precision module
            spikeSelectable.AddInteractionPunch(0.2f);

            // Prepare the next ones!
            currentExpectedIndexInSequence++;

            if (currentExpectedIndexInSequence == 5)
            {
                ModuleLog(true, "Successfully removed the {0} spike in {1}. This was the last one. Module Solved!",
                    cactusGrid[spikeIndex], GetReadableCactusPosition(spikeIndex));
                SolveModule();
            }
            else
            {
                // Get the acceptable positions from the one that was pressed
                expectedNextSpikePressed = GetAvailableCactusMovements(spikeIndex)
                    .Select(x => MoveInDiagramOrCactus(spikeIndex, x, false)) // Next "adjacent" ones
                    .Where(x => IterateSearchThroughSpikeSequence(x, currentExpectedIndexInSequence)) // which still fulfills the sequence
                    .ToList();

                ModuleLog(true, "Successfully removed the {0} spike in {1}", cactusGrid[spikeIndex], GetReadableCactusPosition(spikeIndex));
            }
        }
        else
        {
            ModuleLog(true, "Tried to remove the {0} spike in {1}, but it isn't part of the sequence!! Strike!!",
                cactusGrid[spikeIndex], GetReadableCactusPosition(spikeIndex));
            thisModule.HandleStrike();

            // No cactus removal!
        }
    }


    // =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
    //    Puzzle Generation
    // =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=

    void GeneratePuzzle()
    {
        ManageRuleseed();
        PredetermineColourData();
        DetermineColourSequence();
        GenerateGlassCactusGrid();
    }

    void ManageRuleseed()
    {
        MonoRandom Rng = ruleseedManager.GetRNG();

        ModuleLog(true, "Using Rule Seed {0}:", Rng.Seed);
        if (Rng.Seed == 1)
        {
            manualDiagram = new List<SpikeColour>() 
            {
                SpikeColour.Green, SpikeColour.Blue, SpikeColour.Yellow,    // West column - Red
                SpikeColour.Blue, SpikeColour.Red, SpikeColour.Yellow,      // NW column - Green
                SpikeColour.Red, SpikeColour.Yellow, SpikeColour.Green,     // North column - Blue
                SpikeColour.Blue, SpikeColour.Green, SpikeColour.Red,        // NE column - Yellow
                SpikeColour.Red, SpikeColour.Green, SpikeColour.Yellow, SpikeColour.Blue,   // Outer row, E to SW clockwise
                SpikeColour.Blue, SpikeColour.Yellow, SpikeColour.Red, SpikeColour.Green,   // Middle row
                SpikeColour.Red, SpikeColour.Yellow, SpikeColour.Green, SpikeColour.Blue    // Inner row
            };

            firstColourOccurenceRule = ColourOccurences.Most;
            secondColourOccurenceRule = ColourOccurences.Least;

            return;
        }


        manualDiagram = new List<SpikeColour>();
        // Each column, RGBY, in order.
        // They each contain the other 3 colours
        SpikeColour[] _red = new SpikeColour[3] { SpikeColour.Green, SpikeColour.Blue, SpikeColour.Yellow };
        SpikeColour[] _green = new SpikeColour[3] { SpikeColour.Red, SpikeColour.Blue, SpikeColour.Yellow };
        SpikeColour[] _blue = new SpikeColour[3] { SpikeColour.Red, SpikeColour.Green, SpikeColour.Yellow };
        SpikeColour[] _yellow = new SpikeColour[3] { SpikeColour.Red, SpikeColour.Green, SpikeColour.Blue };

        Rng.ShuffleFisherYates(_red);
        Rng.ShuffleFisherYates(_green);
        Rng.ShuffleFisherYates(_blue);
        Rng.ShuffleFisherYates(_yellow);

        manualDiagram.AddRange(_red);
        manualDiagram.AddRange(_green);
        manualDiagram.AddRange(_blue);
        manualDiagram.AddRange(_yellow);

        // Each row from E to SW clockwise contains all 4 colours
        SpikeColour[] _outer = new SpikeColour[4] { SpikeColour.Red, SpikeColour.Green, SpikeColour.Blue, SpikeColour.Yellow };
        SpikeColour[] _middle = new SpikeColour[4] { SpikeColour.Red, SpikeColour.Green, SpikeColour.Blue, SpikeColour.Yellow };
        SpikeColour[] _inner = new SpikeColour[4] { SpikeColour.Red, SpikeColour.Green, SpikeColour.Blue, SpikeColour.Yellow };

        Rng.ShuffleFisherYates(_outer);
        Rng.ShuffleFisherYates(_middle);
        Rng.ShuffleFisherYates(_inner);

        manualDiagram.AddRange(_outer);
        manualDiagram.AddRange(_middle);
        manualDiagram.AddRange(_inner);

        ModuleLog(false, "Glass Cactus Spikes order is as follows: {0}", manualDiagram.Join());
        ModuleLog(false, "The 4 columns of 3, in order RGBY (west to northeast), then the 3 remaining rows of 4 in clockwise outer-to-inner order.");


        // Which Occurence (Least, SecondLeast, SecondMost, Most) is checked for the starting coordinate of the Cactus
        ColourOccurences[] _occurences = new ColourOccurences[4] { ColourOccurences.Least, ColourOccurences.SecondLeast, ColourOccurences.SecondMost, ColourOccurences.Most};
        Rng.ShuffleFisherYates(_occurences);

        firstColourOccurenceRule = _occurences[0];
        secondColourOccurenceRule = _occurences[1];
        ModuleLog(false, "The spike colours to watch for are {0} and {1}.", firstColourOccurenceRule, secondColourOccurenceRule);
    }

    // Predetermine how many spikes of each colour there are, which colours are present the most or least, all that jazz
    void PredetermineColourData()
    {
        SpikeColour[] _fourColours = new SpikeColour[4] { SpikeColour.Red, SpikeColour.Green, SpikeColour.Blue, SpikeColour.Yellow }.Shuffle();
        colourOccurenceHierarchy = new Dictionary<ColourOccurences, SpikeColour>();
        for (int i = 0; i < 4; i++)
        {
            colourOccurenceHierarchy.Add((ColourOccurences)i, _fourColours[i]);
        }


        // For a better distribution, we have a total of 32 spikes, and we want each colour to have a unique number of spikes,
        // with something like 13-10-6-3, or 12-9-7-4, or 14-9-6-3, with enough space to not have to count everything
        int[][] _colourOccurencePossibilities = new int[4][]
        {
            new int[4] { 3, 6, 10, 13 },
            new int[4] { 4, 7, 9, 12 },
            new int[4] { 3, 6, 9, 14 },
            new int[4] { 5, 7, 9, 11 },
        };

        int[] _selectedColourOccurencePossibility = _colourOccurencePossibilities.PickRandom();

        numberOfSpikesPerColour = new Dictionary<SpikeColour, int>();
        for (int i = 0; i < 4; i ++)
        {
            ColourOccurences _occurence = (ColourOccurences)i;

            numberOfSpikesPerColour.Add(colourOccurenceHierarchy[_occurence], _selectedColourOccurencePossibility[i]);

            ModuleLog(true, "The {0} present Colour is {1}, with {2} spikes.",
                _occurence,
                colourOccurenceHierarchy[_occurence],
                numberOfSpikesPerColour[colourOccurenceHierarchy[_occurence]]);

        }
        
    }


    void DetermineColourSequence()
    {
        // The Diagram is in a very weird but specific order created to make the Rule Seed as easy to generate as possible
        // But we need another form to make it traversable easily!

        // This one will start at the North semi-outer, going clockwise, and then go inner, just like the cactus.
        SpikeColour[] _transposedDiagram = new SpikeColour[24] {
            manualDiagram[6], manualDiagram[9], manualDiagram[12], manualDiagram[13], manualDiagram[14], manualDiagram[15], manualDiagram[0], manualDiagram[3],
            manualDiagram[7], manualDiagram[10], manualDiagram[16], manualDiagram[17], manualDiagram[18], manualDiagram[19], manualDiagram[1], manualDiagram[4],
            manualDiagram[8], manualDiagram[11], manualDiagram[20], manualDiagram[21], manualDiagram[22], manualDiagram[23], manualDiagram[2], manualDiagram[5]
        };


        // Determine the starting location:
        // FirstRule to find which column to use, and SecondRule to know where to stop and walk right
        Dictionary<SpikeColour, int> startingColumns = new Dictionary<SpikeColour, int>() {
            { SpikeColour.Red, 6 },
            { SpikeColour.Green, 7 },
            { SpikeColour.Blue, 0 },
            { SpikeColour.Yellow, 1 }
        };
        SpikeColour spikeStartingColumn = colourOccurenceHierarchy[firstColourOccurenceRule];
        int startingColumn = startingColumns[spikeStartingColumn];

        // Go down the table (+8) until we find the colour of the SecondColourOccurenceRule
        int startingCell = _transposedDiagram[startingColumn] == colourOccurenceHierarchy[secondColourOccurenceRule] ? startingColumn :
            _transposedDiagram[startingColumn + 8] == colourOccurenceHierarchy[secondColourOccurenceRule] ? startingColumn + 8 : startingColumn + 16;

        // Rotate on the same row until we find another spike with the same colour
        int postRotationCell = startingCell;
        int postRotationRow = postRotationCell / 8;
        do { 
            postRotationCell++;
            if (postRotationCell/8 != postRotationRow) { postRotationCell -= 8; }
        }
        while (_transposedDiagram[postRotationCell] != colourOccurenceHierarchy[secondColourOccurenceRule]);



        // Location in the diagram before the first movement is postRotationCell
        // Afterwards, the location after each movement is given by the value in this array
        int[] locationsAfterEachMovement = new int[5];

        // We generate the Breeze Movements dynamically, to ensure it never goes down or up outside of the path inner 3 rings
        // It also never should pass more coloured spikes than exists
        for (int i = 0; i < 5; i++)
        {
            // Get the current location
            int _preLocation = i == 0 ? postRotationCell : locationsAfterEachMovement[i - 1];

            // First make all Rotations available, and depending on context remove some of them
            List<BreezeMovements> _allowedMovements = new List<BreezeMovements>() { BreezeMovements.Up, BreezeMovements.Down, BreezeMovements.Left, BreezeMovements.Right,
                BreezeMovements.UpLeft, BreezeMovements.UpRight, BreezeMovements.DownLeft, BreezeMovements.DownRight };


            // If at the outer row, cannot go up
            int _currentRow = _preLocation / 8;
            if (_currentRow == 0)
            {
                _allowedMovements.Remove(BreezeMovements.Up);
                _allowedMovements.Remove(BreezeMovements.UpRight);
                _allowedMovements.Remove(BreezeMovements.UpLeft);
            }
            // If at the inner row, cannot go down
            else if (_currentRow == 2)
            {
                _allowedMovements.Remove(BreezeMovements.Down);
                _allowedMovements.Remove(BreezeMovements.DownRight);
                _allowedMovements.Remove(BreezeMovements.DownLeft);
            }

            // For ease of display of the Breeze, do not have >= 3 movements
            // going Right or Left without the other one being present
            // so that it doesn't escape a "center square" of sorts inside of the Glass Cactus
            int _leftCount = breezeMovementSequence.Count(x => x.ToString().Contains("Left"));
            int _rightCount = breezeMovementSequence.Count(x => x.ToString().Contains("Right"));
            int _upCount = breezeMovementSequence.Count(x => x.ToString().Contains("Up"));
            int _downCount = breezeMovementSequence.Count(x => x.ToString().Contains("Down"));
            if (_leftCount - _rightCount == 2)
            {
                // 2 more Lefts than Rights: prevent all Lefts!
                _allowedMovements.Remove(BreezeMovements.Left);
                _allowedMovements.Remove(BreezeMovements.UpLeft);
                _allowedMovements.Remove(BreezeMovements.DownLeft);
            }
            else if (_rightCount - _leftCount == 2)
            {
                // 2 more Rights than Lefts
                _allowedMovements.Remove(BreezeMovements.Right);
                _allowedMovements.Remove(BreezeMovements.UpRight);
                _allowedMovements.Remove(BreezeMovements.DownRight);
            }

            if (_upCount - _downCount == 2)
            {
                // 2 more Ups than Downs
                _allowedMovements.Remove(BreezeMovements.Up);
                _allowedMovements.Remove(BreezeMovements.UpRight);
                _allowedMovements.Remove(BreezeMovements.UpLeft);
            }
            else if (_downCount - _upCount == 2)
            {
                // 2 more Downs than Ups
                _allowedMovements.Remove(BreezeMovements.Down);
                _allowedMovements.Remove(BreezeMovements.DownRight);
                _allowedMovements.Remove(BreezeMovements.DownLeft);
            }

            // If a movement makes you land on the Least Colour for the 3rd time, prevent it
            // This is because it'll force the Cactus Generation to have them too close to each other, and make searching
            // for the sequence too hard
            List<BreezeMovements> _allowedCopy = _allowedMovements.ToList();
            foreach (BreezeMovements _m in _allowedCopy)
            {
                if (_transposedDiagram[MoveInDiagramOrCactus(_preLocation, _m, true)] == colourOccurenceHierarchy[ColourOccurences.Least])
                {
                    if (finalSequenceColours.Count(x => x == colourOccurenceHierarchy[ColourOccurences.Least]) == 2)
                    {
                        _allowedMovements.Remove(_m);
                    }
                }
            }


            // Once we land here, we've filtered all unwanted possible movements, and we can just select a new one!
            // There **IS** a world where this _allowedMovements is empty, it happened to me during testing, so we need to address that
            if (_allowedMovements.Count == 0)
            {
                // Reset this entire loop
                i = 0;
                locationsAfterEachMovement = new int[5];
                breezeMovementSequence.Clear();
                finalSequenceColours.Clear();

                // I'm leaving this because I couldn't trigger this edge case after 500+ generations of the module
                // What the heck
                ModuleLog(false, "Reset the entire generation due to an empty _allowedMovement!");

                continue;
            }
            BreezeMovements selectedMovement = _allowedMovements.PickRandom();
            
            // Save that Movement
            breezeMovementSequence.Add(selectedMovement);
            locationsAfterEachMovement[i] = MoveInDiagramOrCactus(_preLocation, selectedMovement, true);
            finalSequenceColours.Add(_transposedDiagram[locationsAfterEachMovement[i]]);
        }


        selectedBreezeRotation = (BreezeRotation)Random.Range(0, 2);


        // Breeze Movements should be logged with the reads, before the table shenanigans
        // So we save all of the data and log everything at the very end

        ModuleLog(true, "Movement sequence of the Breeze is: {0}", breezeMovementSequence.Join());
        ModuleLog(true, "The Breeze rotates {0}", selectedBreezeRotation);

        ModuleLog(true, "Using the column with outermost colour {0}, going inner until {1} and then clockwise to the {1} in {2}",
            spikeStartingColumn, colourOccurenceHierarchy[secondColourOccurenceRule], GetReadableDiagramPosition(postRotationCell));

        for (int i = 0; i < 5; i++)
        {
            ModuleLog(true, "Moving {0} goes to the {1} in {2}.", breezeMovementSequence[i], finalSequenceColours[i],
                GetReadableDiagramPosition(locationsAfterEachMovement[i]));
        }

        ModuleLog(true, "The final sequence to find on the Glass Cactus is {0}; going {1}", finalSequenceColours.Join(), selectedBreezeRotation);
    }

    // If it uses Diagram, it has only 3 rows; otherwise it has 4
    int MoveInDiagramOrCactus(int startingLocation, BreezeMovements movement, bool useDiagram)
    {
        switch (movement)
        {
            case BreezeMovements.Up:
                if (startingLocation / 8 == 0)
                {
                    ModuleLog(true, "An error occured: trying to move Up from {0}. Solving the module preemptively.", startingLocation);
                    SolveModule();
                    return -1;
                }
                return startingLocation - 8;
            case BreezeMovements.Down:
                if (startingLocation / 8 == (useDiagram ? 2 : 3))
                {
                    ModuleLog(true, "An error occured: trying to move Down from {0}. Solving the module preemptively.", startingLocation);
                    SolveModule();
                    return -1;
                }
                return startingLocation + 8;
            
            // Remove the current column (go to column 0), and add (column +1/-1) mod 8
            case BreezeMovements.Left: return startingLocation - (startingLocation % 8) + (startingLocation % 8 + 7) % 8;
            case BreezeMovements.Right: return startingLocation - (startingLocation % 8) + (startingLocation % 8 + 1) % 8;

            // I'm not gonna bother write the logic, move Up then Left
            case BreezeMovements.UpLeft: return MoveInDiagramOrCactus(MoveInDiagramOrCactus(startingLocation, BreezeMovements.Up, useDiagram), BreezeMovements.Left, useDiagram);
            case BreezeMovements.UpRight: return MoveInDiagramOrCactus(MoveInDiagramOrCactus(startingLocation, BreezeMovements.Up, useDiagram), BreezeMovements.Right, useDiagram);
            case BreezeMovements.DownLeft: return MoveInDiagramOrCactus(MoveInDiagramOrCactus(startingLocation, BreezeMovements.Down, useDiagram), BreezeMovements.Left, useDiagram);
            case BreezeMovements.DownRight: return MoveInDiagramOrCactus(MoveInDiagramOrCactus(startingLocation, BreezeMovements.Down, useDiagram), BreezeMovements.Right, useDiagram);
        }
        return -1;
    }

    void GenerateGlassCactusGrid()
    {
        SpikeColour?[] grid = new SpikeColour?[32];

        // Place the initial Sequence anywhere
        // Decide a starting position anywhere in the grid
        int _current = Random.Range(0, 32);

        // Place the colour then move
        for (int i = 0; i < 5; i++)
        {
            grid[_current] = finalSequenceColours[i];
            ModuleLog(true, "{0} spike in the sequence is in {1}", finalSequenceColours[i], GetReadableCactusPosition(_current));

            if (i == 4) { break; }

            _current = MoveInDiagramOrCactus(_current, GetAvailableCactusMovements(_current).PickRandom(), false);
        }
        

        // Fill the rest of the grid
        for (int i = 0; i < 32; i ++)
        {
            if (grid[i].HasValue) { continue; }

            // Select a random starting colour
            int offset = Random.Range(0, 4);

            for (int c = 0; c < 4; c ++)
            {
                SpikeColour col = (SpikeColour)((offset + c) % 4);

                // Do not go over the max count of presence for each colour
                if (grid.Count(x => x == col) == numberOfSpikesPerColour[col])
                { continue; }

                // Place the colour
                grid[i] = col;
            }
        }

        cactusGrid = grid.Select(x => x.Value).ToArray();

        ModuleLog(true, "The Glass Cactus' spikes, starting at the outermost North, going clockwise then inner, is: {0}", cactusGrid.Join());

        return;

        // int reds = finalSequenceColours.Count(x => x == SpikeColour.Red);
        // int greens = finalSequenceColours.Count(x => x == SpikeColour.Green);
        // int blues = finalSequenceColours.Count(x => x == SpikeColour.Blue);
        // int yellows = finalSequenceColours.Count(x => x == SpikeColour.Yellow);
        // 
        // return IterateThroughCactusGrid(grid, new int[4] { reds, greens, blues, yellows }).Select(x => x.Value).ToArray();
    }

    List<BreezeMovements> GetAvailableCactusMovements(int location)
    {
        int row = location / 8;

        if (selectedBreezeRotation == BreezeRotation.Clockwise)
        {
            if (row == 0)
            {
                return new List<BreezeMovements>() { BreezeMovements.Right, BreezeMovements.DownRight };
            }
            else if (row == 3)
            {
                return new List<BreezeMovements>() { BreezeMovements.Right, BreezeMovements.UpRight };
            }
            else
            {
                return new List<BreezeMovements>() { BreezeMovements.Right, BreezeMovements.UpRight, BreezeMovements.DownRight };
            }
        }
        else
        {
            if (row == 0)
            {
                return new List<BreezeMovements>() { BreezeMovements.Left, BreezeMovements.DownLeft };
            }
            else if (row == 3)
            {
                return new List<BreezeMovements>() { BreezeMovements.Left, BreezeMovements.UpLeft };
            }
            else
            {
                return new List<BreezeMovements>() { BreezeMovements.Left, BreezeMovements.UpLeft, BreezeMovements.DownLeft };
            }
        }
    }

    // We have a non-unique solution, so there may be multiple valid starting pins.
    // Once the first one has been pressed, the next are forced
    void DetermineValidFirstPress()
    {
        expectedNextSpikePressed = new List<int>();
        currentExpectedIndexInSequence = 0;

        // Check every spike
        for (int i = 0; i < 32; i++)
        {
            // For every Spike, check if there's at least one valid sequence that starts from it
            if (IterateSearchThroughSpikeSequence(i, 0))
            {
                expectedNextSpikePressed.Add(i);
            }
        }
    }

    // Thanks Quinn Wuest for the code!
    bool IterateSearchThroughSpikeSequence(int spike, int depth)
    {
        // After 5 consecutive valid finds, this sequence is valid
        if (depth == 5) { return true; }

        // Check if the Spike is of the correct next colour in the sequence
        if (cactusGrid[spike] != finalSequenceColours[depth])
        {
            return false;
        }

        // Check every available movement for the subsequent Cactus Spikes, since it can switch rows
        foreach (BreezeMovements m in GetAvailableCactusMovements(spike))
        {
            int nextSpike = MoveInDiagramOrCactus(spike, m, false);
            // Iterate
            if (IterateSearchThroughSpikeSequence(nextSpike, depth+1))
            {
                // If there's just a singular one that's valid, that's all we need!
                return true;
            }
        }

        // If we land here, none of the subsequent movements were valid so we escape
        return false;
    }


    // =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
    //    Visuals
    // =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=

    void SpawnAndManageSpikes()
    {
        spawnedSpikes = new KMSelectable[32];

        float height = 0;
        float slope = 0;
        float distance = 0;
        const float eightTurn = Mathf.PI * 0.25f;

        moduleSelectable.Children = new KMSelectable[32];

        // Spawn all 32 Spikes
        for (int i = 0; i < 32; i ++)
        {
            GameObject spike = Instantiate(SpikePrefab, transform);
            spawnedSpikes[i] = spike.GetComponent<KMSelectable>();

            // Place it around
            int row = i / 8;
            // Put it in the correct "Right is 0, going counterclock" order to use proper trigonometry
            // Instead of 8-i -2, we take advantage of the modulo operator by combining the "8-2"
            int column = i % 8;


            switch (row)
            {
                case 0:
                    height = 0.025f;
                    slope = 75f;
                    distance = 0.07f;
                    break;

                case 1:
                    height = 0.045f;
                    slope = 60f;
                    distance = 0.06f;
                    break;

                case 2:
                    height = 0.0575f;
                    slope = 40f;
                    distance = 0.05f;
                    break;

                case 3:
                    height = 0.068f;
                    slope = 25f;
                    distance = 0.03f;
                    break;
            }

            Vector3 localPos = new Vector3(distance * Mathf.Sin(eightTurn * column), height, distance * Mathf.Cos(eightTurn * column));
            Vector3 localRot = new Vector3(slope, column / 8f * 360f, 0);
            

            spawnedSpikes[i].transform.localPosition = localPos;
            spawnedSpikes[i].transform.localEulerAngles = localRot;

            // Set the Spike's Colour
            spawnedSpikes[i].GetComponent<MeshRenderer>().material.SetColor("_Color", spikeColours[(int)cactusGrid[i]]);

            // Setup its Selectable
            spawnedSpikes[i].Parent = moduleSelectable;
            moduleSelectable.Children.SetValue(spawnedSpikes[i], i);

            // Colourblind Text, Set the text
            GameObject ColourblindLetter = spawnedSpikes[i].transform.GetChild(1).gameObject;
            ColourblindLetter.GetComponent<TextMesh>().text = cactusGrid[i].ToString()[0].ToString();

            // Setup the Interaction
            int u = i; // i gets updated, so we can't pass it in the interaction method or it'll stay at 32 for all of them, we need a clone
            spawnedSpikes[u].OnInteract += delegate () { PressedCactusSpike(u, spawnedSpikes[u]); return false; };
        }

        // Update the Children Properly!
        // Not improperly!!!
        moduleSelectable.UpdateChildrenProperly();

        // Colourblind Text, Set visibility
        ToggleColourblindMode(colorblindManager.ColorblindModeActive);
    }

    IEnumerator RemoveSingleSpikeCoroutine(Transform spikeToRemove)
    {
        Vector3 startingLocation = spikeToRemove.localPosition;
        float timer = 0f;
        const float distance = 0.035f;

        while (timer < 1.0f)
        {
            timer += Time.deltaTime;

            // Adding Vector3.up moves it up relative to the module
            // Adding spikeTransform.up moves it with world coordinates relative to the module
            // So we need to get the spike's up vector in world, and convert that to module space
            Vector3 relativeUp = transform.worldToLocalMatrix * (spikeToRemove.up);

            float progress = Easing.OutQuint(timer, 0, 1, 1.0f);
            spikeToRemove.localPosition = startingLocation + relativeUp * progress * distance;

            yield return null;
        }

        spikeToRemove.gameObject.SetActive(false);
        yield break;
    }

    IEnumerator EjectAllRemainingSpikes()
    {
        yield return new WaitForSeconds(0.8f);

        // Much bigger Interaction Punch!
        spawnedSpikes[24].AddInteractionPunch(0.2f);
        
        
        float timer = 0f;

        while (timer < 1f)
        {
            timer += Time.deltaTime;

            foreach (KMSelectable _s in spawnedSpikes)
            {
                // Simply add its own Up
                _s.transform.position += _s.transform.up * Time.deltaTime * 1f;
            }

            yield return null;
        }

        foreach (KMSelectable _s in spawnedSpikes)
        {
            // Bring back all Spikes to the center to avoid having a stupid bounding box for the module
            // We still care about optimization!
            _s.transform.localPosition = Vector3.zero;
            _s.gameObject.SetActive(false);
        }
        yield break;
    }

    void PrepareVisualBreezeMovementData()
    {
        // The Breeze has "just" some easy movements to show, but I want something that looks GOOD
        // So there's a bunch of data to add afterwards!

        // We know from the generation that we never move more than 2 in any direction from the center.
        // But in the cases where the breeze moves 2 away from the origin, it'll look better if it wasn't offset permanently!
        // So we'll make the "returns" longer to compensate
        Vector2Int totalOffset = new Vector2Int(0, 0);
        foreach (BreezeMovements m in breezeMovementSequence)
        {
            switch (m)
            {
                case BreezeMovements.Up: totalOffset.y++; break;
                case BreezeMovements.Down: totalOffset.y--; break;
                case BreezeMovements.Left: totalOffset.x--; break;
                case BreezeMovements.Right: totalOffset.x++; break;
                case BreezeMovements.UpLeft: totalOffset.x--; totalOffset.y++; break;
                case BreezeMovements.UpRight: totalOffset.x++; totalOffset.y++; break;
                case BreezeMovements.DownLeft: totalOffset.x--; totalOffset.y--; break;
                case BreezeMovements.DownRight: totalOffset.x++; totalOffset.y--; break;
            }
        }

        List<BreezeMovements> movementsToDouble = new List<BreezeMovements>();

        if (totalOffset.y == -2)
        {
            // Do not add the movements that bring us too far on the other axis!
            // You can still have for example UR -> L -> U -> DL -> L
            // It counts as having 2 left and only 1 up, but you go up twice, so doubling would bring you outside of the cactus
            // Better very safe than sorry!
            movementsToDouble.Add(BreezeMovements.Up);
            if (totalOffset.x < 1) { movementsToDouble.Add(BreezeMovements.UpRight); }
            if (totalOffset.x > -1) { movementsToDouble.Add(BreezeMovements.UpLeft); }
        }
        else if(totalOffset.y == 2)
        {
            movementsToDouble.Add(BreezeMovements.Down);
            if (totalOffset.x < 1) { movementsToDouble.Add(BreezeMovements.DownRight); }
            if (totalOffset.x > -1) { movementsToDouble.Add(BreezeMovements.DownLeft); }
        }

        if (totalOffset.x == -2)
        {
            movementsToDouble.Add(BreezeMovements.Right);
            if (totalOffset.y < 1) { movementsToDouble.Add(BreezeMovements.UpRight); }
            if (totalOffset.y > -1) { movementsToDouble.Add(BreezeMovements.DownRight); }
        }
        else if (totalOffset.x == 2)
        {
            movementsToDouble.Add(BreezeMovements.Left);
            if (totalOffset.y < 1) { movementsToDouble.Add(BreezeMovements.UpLeft); }
            if (totalOffset.y > -1) { movementsToDouble.Add(BreezeMovements.DownLeft); }
        }

        BreezeMovementLocalPositions = new Vector3[breezeMovementSequence.Count];


        // By virtue of having a coordinate at 2/-2 and 5 movements, we know that there's only 1 movement
        // that goes in the other direction at most; so we can double all we encounter safely

        Vector3 previousLocation = Vector3.up * startingBreezeHeight;
        for (int i = 0; i < breezeMovementSequence.Count; i++)
        {
            Vector3 toAdd = Vector3.zero;
            switch (breezeMovementSequence[i])
            {
                // Add a random height offset for the fun of it
                case BreezeMovements.Up: toAdd = Vector3.up * Random.value * 0.5f + Vector3.forward; break;
                case BreezeMovements.Down: toAdd = Vector3.up * Random.value * 0.5f + Vector3.back; break;
                case BreezeMovements.Left: toAdd = Vector3.up * Random.value * 0.5f + Vector3.left; break;
                case BreezeMovements.Right: toAdd = Vector3.up * Random.value * 0.5f + Vector3.right; break;
                case BreezeMovements.UpLeft: toAdd = Vector3.up * Random.value * 0.5f + Vector3.forward + Vector3.left; break;
                case BreezeMovements.UpRight: toAdd = Vector3.up * Random.value * 0.5f + Vector3.forward + Vector3.right; break;
                case BreezeMovements.DownLeft: toAdd = Vector3.up * Random.value * 0.5f + Vector3.back + Vector3.left; break;
                case BreezeMovements.DownRight: toAdd = Vector3.up * Random.value * 0.5f + Vector3.back + Vector3.right; break;
            }

            if (movementsToDouble.Distinct().Contains(breezeMovementSequence[i]))
            { toAdd *= 1.6f; } // "Double" the movement!

            // Register the movement
            previousLocation = previousLocation + 0.02f * toAdd;
            BreezeMovementLocalPositions[i] = previousLocation;
        }

        // Start movements!
        currentBreezeMovementIndexShown = 0;
        currentBreezeMovementTimer = 0f;
        currentBreezeMovementState = VisualBreezeMovementState.Spinning;
    }

    void UpdateBreezePosition()
    {
        if (moduleSolved) { return; }

        // While the Breeze has some mighty code to handle its movements, it always rotates "randomly"
        // It's actually a pseudorandom, and smooth, and absolutely crazy

        breezeParticleSystem.transform.localEulerAngles = new Vector3(
            Mathf.Sin(Time.time * BreezeRotationConstant1) * 180,
            Mathf.Cos(Time.time * BreezeRotationConstant2) * 180,
            Mathf.Cos(Time.time) * Mathf.Sin(Time.time) * 180
            );

        // The Breeze needs its data to be initialized before starting to move
        if (currentBreezeMovementState == VisualBreezeMovementState.None)
        { return; }

        currentBreezeMovementTimer += Time.deltaTime;

        switch (currentBreezeMovementState)
        {
            case VisualBreezeMovementState.Moving:
                // Each movement lasts a short time
                if (currentBreezeMovementTimer >= 1.7f)
                {
                    // Switch to Next step
                    currentBreezeMovementTimer = 0f;
                    currentBreezeMovementIndexShown++;

                    if (currentBreezeMovementIndexShown == 5)
                    {
                        // Switch to Spinning
                        currentBreezeMovementState = VisualBreezeMovementState.Spinning;
                    }
                    else
                    {
                        // Just load the next Movement
                        currentBreezeMovementTarget = BreezeMovementLocalPositions[currentBreezeMovementIndexShown];
                    }
                }
                // Continue Moving - Nothing to do as the movement target is already setup
                break;

            case VisualBreezeMovementState.Spinning:
                // Spinning lasts much longer though
                if (currentBreezeMovementTimer >= 7f)
                {
                    // Reset to the center
                    currentBreezeMovementState = VisualBreezeMovementState.PreparingToMove;
                    currentBreezeMovementTimer = 0f;
                    currentBreezeMovementTarget = Vector3.up * startingBreezeHeight;
                }
                else
                {
                    // Continue Spinning - Update the MovementTarget
                    
                    // Distance stays for a while then gets closer to 0
                    float spinLerp = Mathf.Max(0, (currentBreezeMovementTimer - 4f) / 3f);
                    float distance = Mathf.Lerp(0.045f, 0, spinLerp);
                    // Use a negative time to go in reverse direction (clockwise)
                    currentBreezeMovementTarget = new Vector3(
                        Mathf.Cos(currentBreezeMovementTimer * (selectedBreezeRotation == BreezeRotation.Clockwise ? -3 : 3)) * distance,
                        startingBreezeHeight,
                        Mathf.Sin(currentBreezeMovementTimer * (selectedBreezeRotation == BreezeRotation.Clockwise ? -3 : 3)) * distance
                        );
                }
                break;

            case VisualBreezeMovementState.PreparingToMove:
                // That empty idle in the center has a timer still
                if (currentBreezeMovementTimer >= 1f)
                {
                    // Switch back to the first Breeze Movement!
                    currentBreezeMovementState = VisualBreezeMovementState.Moving;

                    currentBreezeMovementIndexShown = 0;
                    currentBreezeMovementTimer = 0f;
                    currentBreezeMovementTarget = BreezeMovementLocalPositions[0];
                }
                break;
        }

        // Move towards the Target
        breezeParticleSystem.transform.localPosition = Vector3.Lerp(breezeParticleSystem.transform.localPosition, currentBreezeMovementTarget, Time.deltaTime * 2f);
    }

    IEnumerator FadeOutBreeze()
    {
        float timer = 0;
        
        // Create a clone of the material for the fade out only when solved
        ParticleSystemRenderer _renderer = breezeParticleSystem.GetComponent<ParticleSystem>().GetComponent<ParticleSystemRenderer>();
        Material _mat = Instantiate(_renderer.sharedMaterial);
        _renderer.sharedMaterial = _mat;

        while (timer < 1.5f)
        {
            timer += Time.deltaTime;
            _mat.SetFloat("_Alpha", 1 - (timer / 1.5f));
            yield return null;
        }

        breezeParticleSystem.GetComponent<ParticleSystem>().Stop();
        breezeParticleSystem.gameObject.SetActive(false);
    }

    void ToggleColourblindMode(bool newMode)
    {
        isColorblindModeActive = newMode;

        for (int i = 0; i < 32; i ++)
        {
            GameObject ColourblindLetter = spawnedSpikes[i].transform.GetChild(1).gameObject;
            ColourblindLetter.SetActive(isColorblindModeActive);
        }
    }

    // =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
    //    Other Methods
    // =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=

    void SolveModule()
    {
        moduleSolved = true;
        thisModule.HandlePass();

        PlaySoundAndForget(SolveAudio);
        StartCoroutine(EjectAllRemainingSpikes());
        StartCoroutine(FadeOutBreeze());
    }

    /// <summary> Simplified method to just play a sound from an AudioClip and forget about it </summary>
    void PlaySoundAndForget(AudioClip soundToPlay)
    {
        audioManager.PlaySoundAtTransform(soundToPlay.name, transform);
    }

    // =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
    //    Logging
    // =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=

    string GetReadableDiagramPosition(int spikePosition)
    {
        int column = spikePosition % 8;
        int row = spikePosition / 8; // Outer-to-Inner

        string[] columnNames = new string[8] { "North", "North-East", "East", "South-East", "South", "South-West", "West", "North-West" };
        string[] rowNames = new string[3] { "outer circle", "middle circle", "inner circle" };

        return string.Format("{0} {1}", columnNames[column], rowNames[row]);
    }

    string GetReadableCactusPosition(int spikePosition)
    {
        int column = spikePosition % 8;
        int row = spikePosition / 8; // Outer-to-Inner

        string[] columnNames = new string[8] { "North", "North-East", "East", "South-East", "South", "South-West", "West", "North-West" };
        string[] rowNames = new string[4] { "outermost circle", "middle-outer circle", "middle-inner circle", "innermost circle" };

        return string.Format("{0} {1}", columnNames[column], rowNames[row]);
    }

    void ModuleLog(bool LogInLfa, string message, params object[] args)
    {
        if (LogInLfa)
        { Debug.LogFormat("[Glass Cactus #{0}] {1}", moduleId, string.Format(message, args)); }
        else
        { Debug.LogFormat("<Glass Cactus #{0}> {1}", moduleId, string.Format(message, args)); }
    }


    // =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
    //    Twitch Plays
    // =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=

#pragma warning disable IDE0051 // Remove unused private members
    private readonly string TwitchHelpMessage = @"“!{0} Remove TL2 W3 BL4 B3 SE2” to remove those spikes. Coordinates allow TopLeft, BottomRight, NorthWest, SouthEast... For rows, they are numbered 1-4 from outermost to innermost. “!{0} Showspikes” to rotate the module to see Spikes. “!{0} Cb” to toggle Colourblind Mode.";

    IEnumerator ProcessTwitchCommand(string command)
    {
        // Credit to Royal_Flu$h for this line 
        var commandParts = command.ToLowerInvariant().Split(new[] { ' ', ',', ';', '-' }, StringSplitOptions.RemoveEmptyEntries).ToList();

        if (commandParts.Count == 0)
        { }

        ModuleLog(true, "Received the command ''{0}''", command);

        if (commandParts[0] == "showspikes" || commandParts[0] == "showspike")
        {
            ModuleLog(false, "Rotating the bomb to show the Spikes!");
            
            // Mark the command as valid
            yield return null;

            // Wait a bit to make sure the bomb is settled on a fixed rotation
            yield return new WaitForSeconds(1f);

            float timer = 0;
            float rotationZ, rotationX;
            const float rotationFrequency = 2f * Mathf.PI / 10f;
            const float rotationAmplitude = 20f;

            // Rotate for a bit
            while (timer < 10f)
            {
                timer += Time.deltaTime;

                rotationZ = Mathf.Sin(timer * rotationFrequency) * rotationAmplitude;
                rotationX = Mathf.Cos(timer * rotationFrequency) * rotationAmplitude;

                // Return a Quaternion to rotate the bomb
                // The rotation is RELATIVE to the rotation of the bomb at the START of the command
                // Just give it an offset of "looking at the module straight-on"
                yield return Quaternion.Euler(rotationX, 0, rotationZ);

                // Wait for next frame
                yield return new WaitForEndOfFrame();
            }

            // Bomb automatically resets to its starting rotation at the end
            yield break;
        }

        if (commandParts[0] == "colorblind" || commandParts[0] == "colourblind" || commandParts[0] == "cb" || commandParts[0] == "color" || commandParts[0] == "colour")
        {
            // Mark the command as valid
            yield return null;

            // Toggle
            ToggleColourblindMode(isColorblindModeActive ^ true);
            yield break;
        }

        if (commandParts[0] == "remove" || commandParts[0] == "r" || commandParts[0] == "submit")
        {
            // Remove the first command
            commandParts.RemoveAt(0);

            List<int> _presses = new List<int>();

            // Prepare variables 
            int row = 0;
            int col = 0;

            foreach (string spike in commandParts)
            {
                // No non-unique characters!!!
                if (spike.Distinct().Count() != spike.Length)
                {
                    ModuleLog(false, "Spike Coordinate with duplicate ''{0}'' received. Aborting submission.", spike);
                    yield return "sendtochaterror {0} Received unknown spike coordinate: '" + spike + "'. Aborting submission";
                    yield break;
                }

                // Command must be either a singular letter from TBLRUDNSEW
                // or two letters, using first TBUDNS and then LREW
                // Always must be followed by a number
                Match _m = Regex.Match(spike, @"^([LREW]|[UDTBNS][LREW]?)([1-4])$", RegexOptions.IgnoreCase);

                if (_m.Success == false)
                {
                    ModuleLog(false, "Spike Coordinate ''{0}'' failed passing the Regex. Aborting submission.", spike);
                    yield return "sendtochaterror {0} Received unknown spike coordinate: '" + spike + "'. Aborting submission";
                    yield break;
                }

                // The result of the Capturing Groups is stored in Groups, not in Captures
                // Thanks, RegEx.
                switch (_m.Groups[1].Value)
                {
                    case "u": case "t": case "n": col = 0; break;
                    case "r": case "e": col = 2; break;
                    case "b": case "d": case "s": col = 4; break;
                    case "l": case "w": col = 6; break;

                    case "ur": case "tr": case "nr": case "ue": case "te": case "ne": col = 1; break;
                    case "dr": case "br": case "sr": case "de": case "be": case "se": col = 3; break;
                    case "ul": case "tl": case "nl": case "uw": case "tw": case "nw": col = 7; break;
                    case "dl": case "bl": case "sl": case "dw": case "bw": case "sw": col = 5; break;

                    default:
                        ModuleLog(false, "Couldn't parse Capture Group 0 ''{0}'' for spike ''{1}''. Aborting submission.", _m.Captures[0].Value, spike);
                        yield return "sendtochaterror {0} Received unknown spike coordinate: '" + spike + "'. Aborting submission";
                        yield break;
                }

                row = int.Parse(_m.Groups[2].Value) - 1;

                _presses.Add(8 * row + col);
            }

            // This only gets called if all previous command parts were for a valid submission!!
            yield return null;
            foreach (int spikeIndex in _presses)
            {
                spawnedSpikes[spikeIndex].OnInteract();
                yield return new WaitForSeconds(0.2f);
            }
            yield break;
        }

        ModuleLog(false, "Command was unrecognized");
        yield return "sendtochaterror {0} Received unknown command. Please don't forget the ''Remove'' command before submitting Spikes.";
        yield break;
    }

    IEnumerator TwitchHandleForcedSolve()
    {
        ModuleLog(true, "Received Twitch Plays Forced Solve");

        yield return null;

        for (int i = currentExpectedIndexInSequence; i < 5; i ++)
        {
            yield return new WaitForSeconds(0.2f);

            // expectedNextSpikePressed contains all acceptable spikes, just get the first one
            spawnedSpikes[expectedNextSpikePressed[0]].OnInteract();
        }
    }

#pragma warning restore IDE0051 // Remove unused private members
}




/*
 * 
 * This generation algorithm is way, WAY too complex, just to ensure a unique solution
 * I don't care about unique solutions enough to approve of this
 * 
 * 
 *     // Inspired by the generation code of Nonbinary Puzzle by Timwi
    SpikeColour?[] IterateThroughCactusGrid(SpikeColour?[] currentGrid, int[] colourAmountsRGBY)
    {
        // If we've placed 32 values, we have a full grid!
        if (colourAmountsRGBY[0] + colourAmountsRGBY[1] + colourAmountsRGBY[2] + colourAmountsRGBY[3] == 32)
        { return currentGrid; }


        // Optimization pass: prioritize filling in the cells that have constraints
        // This means ???

        int cell = 0;


        // Place the colours randomly
        int offset = Random.Range(0, 4);

        // Try all 4 colours
        for (int i = 0; i < 4; i ++)
        {
            SpikeColour _colour = (SpikeColour)((i + offset) % 4);

            // If there are already enough of this colour in the grid, skip to the next
            if (colourAmountsRGBY[(int)_colour] == numberOfSpikesPerColour[_colour])
            { continue; }


            // If this colour is not in the sequence, automatically accept the proposal as it cannot create a new sequence
            // so verifying everything else isn't necessary
            if (finalSequenceColours.Contains(_colour) == false)
            {
                currentGrid[cell] = _colour;
                colourAmountsRGBY[(int)_colour]++;
                if (IterateThroughCactusGrid(currentGrid, colourAmountsRGBY) != null)
                {
                    // If this is valid, then we have a fully valid grid so we just return everything!
                    return currentGrid;
                }

                // If we're here, this colour WAS valid but all possibilities down the road were incorrect;
                // so immediately skip to the next colour
                currentGrid[cell] = null;
                colourAmountsRGBY[(int)_colour]--;
                continue;
            }

            // Make sure this doesn't create a secondary sequence
            // The sequence being 5 long, Any spike anywhere can be the start of a sequence containing this one, even on the opposite side
            // So we have to check them all, no cheat here.
            if (hasGridOnlyOneSequence(currentGrid))
            {
                currentGrid[cell] = _colour;
                colourAmountsRGBY[(int)_colour]++;
                if (IterateThroughCactusGrid(currentGrid, colourAmountsRGBY) != null)
                {
                    // If this is valid, then we have a fully valid grid so we just return everything!
                    return currentGrid;
                }
            }

            // If we're here, this colour WAS valid but all possibilities down the road were incorrect;
            // so immediately skip to the next colour
            currentGrid[cell] = null;
            colourAmountsRGBY[(int)_colour]--;
            continue;
        }

        // If we land here, we tried the 4 colours in vain
        return null;
    }

    
    bool hasGridOnlyOneSequence(SpikeColour?[] grid)
    {
        bool foundFirstSequence = false;

        for (int c = 0; c < 32; c++)
        {
            // First cell of the sequence?
            if (grid[c] == finalSequenceColours[0])
            {
                foreach (BreezeMovements m in GetAvailableCactusMovements(c))
                {
                    // Second cell of the sequence?
                    int c1 = MoveInDiagramOrCactus(c, m, false);
                    if (grid[c1] == finalSequenceColours[1])
                    {
                        foreach (BreezeMovements n in GetAvailableCactusMovements(c1))
                        {
                            // Third cell of the sequence?
                            int c2 = MoveInDiagramOrCactus(c1, n, false);
                            if (grid[c2] == finalSequenceColours[2])
                            {
                                foreach (BreezeMovements o in GetAvailableCactusMovements(c2))
                                {
                                    // Fourth cell of the sequence?
                                    int c3 = MoveInDiagramOrCactus(c2, o, false);
                                    if (grid[c3] == finalSequenceColours[3])
                                    {
                                        foreach (BreezeMovements p in GetAvailableCactusMovements(c3))
                                        {
                                            // Fourth cell of the sequence?
                                            int c4 = MoveInDiagramOrCactus(c3, p, false);
                                            if (grid[c4] == finalSequenceColours[4])
                                            {
                                                if (foundFirstSequence)
                                                {
                                                    return false;
                                                }
                                                foundFirstSequence = true;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

        }

        return true;
    }
 * 
 * 
 */