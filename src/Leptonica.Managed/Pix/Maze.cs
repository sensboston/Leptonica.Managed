using System;

namespace Leptonica;

/*
 *  maze.c: Binary and gray maze generation and search.
 *
 *      This is a game with a pedagogical slant.  A maze is represented
 *      by a binary image.  The ON pixels (fg) are walls.  The goal is
 *      to navigate on OFF pixels (bg), using Manhattan steps
 *      (N, S, E, W), between arbitrary start and end positions.
 *      The problem is thus to find the shortest route between two points
 *      in a binary image that are 4-connected in the bg.  This is done
 *      with a breadth-first search, implemented with a queue.
 *      We also use a queue of pointers to generate the maze (image).
 *
 *          Pix.GenerateBinaryMaze (generateBinaryMaze)
 *          static MazeEl (mazeelCreate)
 *
 *          Pix.SearchBinaryMaze (pixSearchBinaryMaze)
 *          static LocalSearchForBackground (localSearchForBackground)
 *
 *      Generalizing a maze to a grayscale image, the search is
 *      now for the "shortest" or least cost path, for some given
 *      cost function.
 *
 *          Pix.SearchGrayMaze (pixSearchGrayMaze)
 *
 *  Differences from C:
 *    - generateBinaryMaze() seeds the generator with srand(28 * 333) only
 *      under _WIN32 in C.  The goldens come from the MSVC build, so the seed
 *      is always set here (CRand reproduces the MSVC generator).
 *    - The raster line pointer arrays are replaced by row offsets into the pix data.
 */
public sealed partial class Pix
{
    private const int MinMazeWidth = 50;
    private const int MinMazeHeight = 50;

    private const float DefaultWallProbability = 0.65f;
    private const float DefaultAnisotropyRatio = 0.25f;

    /* direction from parent to newly created element */
    private const int START_LOC = 0;
    private const int DIR_NORTH = 1;
    private const int DIR_SOUTH = 2;
    private const int DIR_WEST = 3;
    private const int DIR_EAST = 4;

    /// <summary>Element of the queue and of the heap in the maze functions (struct MazeElement).</summary>
    private sealed class MazeEl : IHeapItem
    {
        public float distance;
        public int x;
        public int y;
        public uint val;   /* value of maze pixel at this location */
        public int dir;    /* direction from parent to child */

        public float HeapKey => distance;
    }

    /*---------------------------------------------------------------------*
     *             Binary maze generation as cellular automaton            *
     *---------------------------------------------------------------------*/
    // leptonica: generateBinaryMaze (maze.c)
    /// <summary>Generates a binary maze.</summary>
    /// <param name="w">width of maze</param>
    /// <param name="h">height of maze</param>
    /// <param name="xi">initial location</param>
    /// <param name="yi">initial location</param>
    /// <param name="wallps">probability that a pixel to the side is ON</param>
    /// <param name="ranis">ratio of prob that pixel in forward direction
    ///     is a wall to the probability that pixel in
    ///     side directions is a wall</param>
    /// <remarks>
    /// (1) We have two input probability factors that determine the
    ///     density of walls and average length of straight passages.
    ///     When ranis &lt; 1.0, you are more likely to generate a wall
    ///     to the side than going forward.  Enter 0.0 for either if
    ///     you want to use the default values.
    /// (2) This is a type of percolation problem, and exhibits
    ///     different phases for different parameters wallps and ranis.
    ///     For larger values of these parameters, regions in the maze
    ///     are not explored because the maze generator walls them
    ///     off and cannot get through.  The boundary between the
    ///     two phases in this two-dimensional parameter space goes
    ///     near these values:
    ///           wallps       ranis
    ///           0.35         1.00
    ///           0.40         0.85
    ///           0.45         0.70
    ///           0.50         0.50
    ///           0.55         0.40
    ///           0.60         0.30
    ///           0.65         0.25
    ///           0.70         0.19
    ///           0.75         0.15
    ///           0.80         0.11
    /// (3) Because there is a considerable amount of overhead in calling
    ///     pixGetPixel() and pixSetPixel(), this function can be sped
    ///     up with little effort using raster line pointers and the
    ///     GET_DATA* and SET_DATA* macros.
    /// </remarks>
    public static Pix GenerateBinaryMaze(int w, int h, int xi, int yi, float wallps, float ranis)
    {
        /* On Windows, seeding is apparently necessary to get decent mazes.
         * Windows rand() returns a value up to 2^15 - 1, whereas unix
         * rand() returns a value up to 2^31 - 1.  Therefore the generated
         * mazes will differ on the two platforms. */
        CRand.Seed(28 * 333);

        if (w < MinMazeWidth)
            w = MinMazeWidth;
        if (h < MinMazeHeight)
            h = MinMazeHeight;
        if (xi <= 0 || xi >= w)
            xi = w / 6;
        if (yi <= 0 || yi >= h)
            yi = h / 5;
        if (wallps < 0.05 || wallps > 0.95)
            wallps = DefaultWallProbability;
        if (ranis < 0.05 || ranis > 1.0)
            ranis = DefaultAnisotropyRatio;
        float wallpf = wallps * ranis;

            /* These are initialized to OFF */
        Pix pixd = Create(w, h, 1);  /* the destination maze */
        Pix pixm = Create(w, h, 1);  /* for bookkeeping, to indicate pixels already visited */

        L_Queue<MazeEl> lq = L_Queue<MazeEl>.Create(0);

            /* Prime the queue with the first pixel; it is OFF */
        MazeEl el = MazeelCreate(xi, yi, START_LOC);
        pixm.SetPixel(xi, yi, 1);  /* mark visited */
        lq.Add(el);

            /* While we're at it ... */
        uint val;
        float frand, testp;
        while (lq.Count > 0) {
            MazeEl elp = lq.Remove()!;
            int x = elp.x;
            int y = elp.y;
            int dir = elp.dir;
            if (x > 0) {  /* check west */
                pixm.GetPixel(x - 1, y, out val);
                if (val == 0) {  /* not yet visited */
                    pixm.SetPixel(x - 1, y, 1);  /* mark visited */
                    frand = (float)CRand.Next() / (float)CRand.RandMax;
                    testp = wallps;
                    if (dir == DIR_WEST)
                        testp = wallpf;
                    if (frand <= testp) {  /* make it a wall */
                        pixd.SetPixel(x - 1, y, 1);
                    } else {  /* not a wall */
                        el = MazeelCreate(x - 1, y, DIR_WEST);
                        lq.Add(el);
                    }
                }
            }
            if (y > 0) {  /* check north */
                pixm.GetPixel(x, y - 1, out val);
                if (val == 0) {  /* not yet visited */
                    pixm.SetPixel(x, y - 1, 1);  /* mark visited */
                    frand = (float)CRand.Next() / (float)CRand.RandMax;
                    testp = wallps;
                    if (dir == DIR_NORTH)
                        testp = wallpf;
                    if (frand <= testp) {  /* make it a wall */
                        pixd.SetPixel(x, y - 1, 1);
                    } else {  /* not a wall */
                        el = MazeelCreate(x, y - 1, DIR_NORTH);
                        lq.Add(el);
                    }
                }
            }
            if (x < w - 1) {  /* check east */
                pixm.GetPixel(x + 1, y, out val);
                if (val == 0) {  /* not yet visited */
                    pixm.SetPixel(x + 1, y, 1);  /* mark visited */
                    frand = (float)CRand.Next() / (float)CRand.RandMax;
                    testp = wallps;
                    if (dir == DIR_EAST)
                        testp = wallpf;
                    if (frand <= testp) {  /* make it a wall */
                        pixd.SetPixel(x + 1, y, 1);
                    } else {  /* not a wall */
                        el = MazeelCreate(x + 1, y, DIR_EAST);
                        lq.Add(el);
                    }
                }
            }
            if (y < h - 1) {  /* check south */
                pixm.GetPixel(x, y + 1, out val);
                if (val == 0) {  /* not yet visited */
                    pixm.SetPixel(x, y + 1, 1);  /* mark visited */
                    frand = (float)CRand.Next() / (float)CRand.RandMax;
                    testp = wallps;
                    if (dir == DIR_SOUTH)
                        testp = wallpf;
                    if (frand <= testp) {  /* make it a wall */
                        pixd.SetPixel(x, y + 1, 1);
                    } else {  /* not a wall */
                        el = MazeelCreate(x, y + 1, DIR_SOUTH);
                        lq.Add(el);
                    }
                }
            }
        }

        return pixd;
    }

    // leptonica: mazeelCreate (maze.c)
    private static MazeEl MazeelCreate(int x, int y, int dir)
    {
        return new MazeEl { x = x, y = y, dir = dir };
    }

    /*---------------------------------------------------------------------*
     *                           Binary maze search                        *
     *---------------------------------------------------------------------*/
    // leptonica: pixSearchBinaryMaze (maze.c)
    /// <summary>Finds the shortest path between two points in a binary maze.</summary>
    /// <param name="xi">beginning point; use same initial point
    ///     that was used to generate the maze</param>
    /// <param name="yi">beginning point; use same initial point
    ///     that was used to generate the maze</param>
    /// <param name="xf">end point, or close to it</param>
    /// <param name="yf">end point, or close to it</param>
    /// <param name="pixd">maze with path illustrated, or
    ///     if no path possible, the part of the maze
    ///     that was searched</param>
    /// <returns>pta shortest path, or null if no path exists</returns>
    /// <remarks>
    /// (1) Because of the overhead in calling pixGetPixel() and
    ///     pixSetPixel(), we have used raster line pointers and the
    ///     GET_DATA* and SET_DATA* macros for many of the pix accesses.
    /// (2) Commentary:
    ///       The goal is to find the shortest path between beginning and
    ///     end points, without going through walls, and there are many
    ///     ways to solve this problem.
    ///       We use a queue to implement a breadth-first search.  Two auxiliary
    ///     "image" data structures can be used: one to mark the visited
    ///     pixels and one to give the direction to the parent for each
    ///     visited pixel.  The first structure is used to avoid putting
    ///     pixels on the queue more than once, and the second is used
    ///     for retracing back to the origin, like the breadcrumbs in
    ///     Hansel and Gretel.  Each pixel taken off the queue is destroyed
    ///     after it is used to locate the allowed neighbors.  In fact,
    ///     only one distance image is required, if you initialize it
    ///     to some value that signifies "not yet visited."  (We use
    ///     a binary image for marking visited pixels because it is clearer.)
    ///     This method for a simple search of a binary maze is implemented in
    ///     pixSearchBinaryMaze().
    ///       An alternative method would store the (manhattan) distance
    ///     from the start point with each pixel on the queue.  The children
    ///     of each pixel get a distance one larger than the parent.  These
    ///     values can be stored in an auxiliary distance map image
    ///     that is constructed simultaneously with the search.  Once the
    ///     end point is reached, the distance map is used to backtrack
    ///     along a minimum path.  There may be several equal length
    ///     minimum paths, any one of which can be chosen this way.
    /// </remarks>
    public Pta? SearchBinaryMaze(int xi, int yi, int xf, int yf, out Pix pixd)
    {
        const string func = "pixSearchBinaryMaze";
        Pix pixs = this;
        pixs.GetDimensions(out int w, out int h, out int d);
        if (d != 1)
            throw Lept.Arg(func, "pixs not 1 bpp");
        if (xi <= 0 || xi >= w)
            throw Lept.Arg(func, "xi not valid");
        if (yi <= 0 || yi >= h)
            throw Lept.Arg(func, "yi not valid");
        pixs.GetPixel(xi, yi, out uint val);
        if (val != 0)
            throw Lept.Arg(func, "(xi,yi) not bg pixel");
        Pta? pta = null;

            /* Find a bg pixel near input point (xf, yf) */
        LocalSearchForBackground(pixs, ref xf, ref yf, 5);

        Pix pixm = Create(w, h, 1);  /* initialized to OFF; indicates pixels already visited */
        Pix pixp = Create(w, h, 8);  /* direction to parent stored as enum val */
        uint[] lines1 = pixs.Data;
        uint[] linem1 = pixm.Data;
        uint[] linep8 = pixp.Data;
        int wpls = pixs.Wpl, wplm = pixm.Wpl, wplp = pixp.Wpl;

        L_Queue<MazeEl> lq = L_Queue<MazeEl>.Create(0);

            /* Prime the queue with the first pixel; it is OFF */
        MazeEl el = MazeelCreate(xi, yi, 0);  /* don't need direction here */
        pixm.SetPixel(xi, yi, 1);  /* mark visited */
        lq.Add(el);

            /* Fill up the pix storing directions to parents,
             * stopping when we hit the point (xf, yf)  */
        bool found = false;
        while (lq.Count > 0) {
            MazeEl elp = lq.Remove()!;
            int x = elp.x;
            int y = elp.y;
            if (x == xf && y == yf) {
                found = true;
                break;
            }

            if (x > 0) {  /* check to west */
                val = (uint)ArrayAccess.GetDataBit(linem1.AsSpan(y * wplm, wplm), x - 1);
                if (val == 0) {  /* not yet visited */
                    ArrayAccess.SetDataBit(linem1.AsSpan(y * wplm, wplm), x - 1);  /* mark visited */
                    val = (uint)ArrayAccess.GetDataBit(lines1.AsSpan(y * wpls, wpls), x - 1);
                    if (val == 0) {  /* bg, not a wall */
                        ArrayAccess.SetDataByte(linep8.AsSpan(y * wplp, wplp), x - 1, DIR_EAST);  /* parent E */
                        el = MazeelCreate(x - 1, y, 0);
                        lq.Add(el);
                    }
                }
            }
            if (y > 0) {  /* check north */
                val = (uint)ArrayAccess.GetDataBit(linem1.AsSpan((y - 1) * wplm, wplm), x);
                if (val == 0) {  /* not yet visited */
                    ArrayAccess.SetDataBit(linem1.AsSpan((y - 1) * wplm, wplm), x);  /* mark visited */
                    val = (uint)ArrayAccess.GetDataBit(lines1.AsSpan((y - 1) * wpls, wpls), x);
                    if (val == 0) {  /* bg, not a wall */
                        ArrayAccess.SetDataByte(linep8.AsSpan((y - 1) * wplp, wplp), x, DIR_SOUTH);  /* parent S */
                        el = MazeelCreate(x, y - 1, 0);
                        lq.Add(el);
                    }
                }
            }
            if (x < w - 1) {  /* check east */
                val = (uint)ArrayAccess.GetDataBit(linem1.AsSpan(y * wplm, wplm), x + 1);
                if (val == 0) {  /* not yet visited */
                    ArrayAccess.SetDataBit(linem1.AsSpan(y * wplm, wplm), x + 1);  /* mark visited */
                    val = (uint)ArrayAccess.GetDataBit(lines1.AsSpan(y * wpls, wpls), x + 1);
                    if (val == 0) {  /* bg, not a wall */
                        ArrayAccess.SetDataByte(linep8.AsSpan(y * wplp, wplp), x + 1, DIR_WEST);  /* parent W */
                        el = MazeelCreate(x + 1, y, 0);
                        lq.Add(el);
                    }
                }
            }
            if (y < h - 1) {  /* check south */
                val = (uint)ArrayAccess.GetDataBit(linem1.AsSpan((y + 1) * wplm, wplm), x);
                if (val == 0) {  /* not yet visited */
                    ArrayAccess.SetDataBit(linem1.AsSpan((y + 1) * wplm, wplm), x);  /* mark visited */
                    val = (uint)ArrayAccess.GetDataBit(lines1.AsSpan((y + 1) * wpls, wpls), x);
                    if (val == 0) {  /* bg, not a wall */
                        ArrayAccess.SetDataByte(linep8.AsSpan((y + 1) * wplp, wplp), x, DIR_NORTH);  /* parent N */
                        el = MazeelCreate(x, y + 1, 0);
                        lq.Add(el);
                    }
                }
            }
        }

        pixd = pixs.UnpackBinary(32, 1);
        ComposeRGBPixel(255, 0, 0, out uint rpixel);  /* start point */
        ComposeRGBPixel(0, 255, 0, out uint gpixel);
        ComposeRGBPixel(0, 0, 255, out uint bpixel);  /* end point */

        if (found) {
            Log.Info(func, " Path found");
            pta = Pta.Create(0);
            int x = xf;
            int y = yf;
            while (true) {
                pta.AddPt(x, y);
                if (x == xi && y == yi)
                    break;
                pixd.SetPixel(x, y, gpixel);  /* write 'gpixel' onto the path */
                pixp.GetPixel(x, y, out val);
                if (val == DIR_NORTH)
                    y--;
                else if (val == DIR_SOUTH)
                    y++;
                else if (val == DIR_EAST)
                    x++;
                else if (val == DIR_WEST)
                    x--;
            }
        } else {
            Log.Info(func, " No path found");
            /* paint all visited locations */
            for (int i = 0; i < h; i++) {
                ReadOnlySpan<uint> linep = linep8.AsSpan(i * wplp, wplp);
                for (int j = 0; j < w; j++) {
                    if (ArrayAccess.GetDataByte(linep, j) != 0)
                        pixd.SetPixel(j, i, gpixel);
                }
            }
        }
        pixd.SetPixel(xi, yi, rpixel);
        pixd.SetPixel(xf, yf, bpixel);

        return pta;
    }

    // leptonica: localSearchForBackground (maze.c)
    /// <summary>Looks for a background pixel near (<paramref name="px"/>, <paramref name="py"/>) and moves the position to it.</summary>
    /// <param name="pix">The 1 bpp image to search.</param>
    /// <param name="px">Starting x position for the search; receives the found position.</param>
    /// <param name="py">Starting y position for the search; receives the found position.</param>
    /// <param name="maxrad">max distance to search from starting location</param>
    /// <returns>true if bg pixel found; false if not found</returns>
    private static bool LocalSearchForBackground(Pix pix, ref int px, ref int py, int maxrad)
    {
        int x = px;
        int y = py;
        pix.GetPixel(x, y, out uint val);
        if (val == 0) return true;

            /* For each value of r, restrict the search to the boundary
             * pixels in a square centered on (x,y), clipping to the
             * image boundaries if necessary.  */
        pix.GetDimensions(out int w, out int h, out _);
        for (int r = 1; r < maxrad; r++) {
            for (int i = -r; i <= r; i++) {
                if (y + i < 0 || y + i >= h)
                    continue;
                for (int j = -r; j <= r; j++) {
                    if (x + j < 0 || x + j >= w)
                        continue;
                    if (Math.Abs(i) != r && Math.Abs(j) != r)  /* not on "r ring" */
                        continue;
                    pix.GetPixel(x + j, y + i, out val);
                    if (val == 0) {
                        px = x + j;
                        py = y + i;
                        return true;
                    }
                }
            }
        }
        return false;
    }

    /*---------------------------------------------------------------------*
     *                            Gray maze search                         *
     *---------------------------------------------------------------------*/
    // leptonica: pixSearchGrayMaze (maze.c)
    /// <summary>Finds the least cost path between two points in a gray image.</summary>
    /// <param name="xi">beginning point; use same initial point
    ///     that was used to generate the maze</param>
    /// <param name="yi">beginning point; use same initial point
    ///     that was used to generate the maze</param>
    /// <param name="xf">end point, or close to it</param>
    /// <param name="yf">end point, or close to it</param>
    /// <param name="pixd">maze with path illustrated</param>
    /// <returns>pta shortest path</returns>
    /// <remarks>
    /// Commentary:
    ///     Consider first a slight generalization of the binary maze
    ///     search problem.  Suppose that you can go through walls,
    ///     but the cost is higher say, an increment of 3 to go into
    ///     a wall pixel rather than 1?  You're still trying to find
    ///     the shortest path.  One way to do this is with an ordered
    ///     queue, and a simple way to visualize an ordered queue is as
    ///     a set of stacks, each stack being marked with the distance
    ///     of each pixel in the stack from the start.  We place the
    ///     start pixel in stack 0, pop it, and process its 4 children.
    ///     Each pixel is given a distance that is incremented from that
    ///     of its parent 0 in this case, depending on if it is a wall
    ///     pixel or not.  That value may be recorded on a distance map,
    ///     according to the algorithm below.  For children of the first
    ///     pixel, those not on a wall go in stack 1, and wall
    ///     children go in stack 3.  Stack 0 being emptied, the process
    ///     then continues with pixels being popped from stack 1.
    ///     Here is the algorithm for each child pixel.  The pixel's
    ///     distance value, were it to be placed on a stack, is compared
    ///     with the value for it that is on the distance map.  There
    ///     are three possible cases:
    ///        1 If the pixel has not yet been registered, it is pushed
    ///            on its stack and the distance is written to the map.
    ///        2 If it has previously been registered with a higher distance,
    ///            the distance on the map is relaxed to that of the
    ///            current pixel, which is then placed on its stack.
    ///        3 If it has previously been registered with an equal
    ///            or lower value, the pixel is discarded.
    ///     The pixels are popped and processed successively from
    ///     stack 1, and when stack 1 is empty, popping starts on stack 2.
    ///     This continues until the destination pixel is popped off
    ///     a stack.   The minimum path is then derived from the distance map,
    ///     going back from the end point as before.  This is just Dijkstra's
    ///     algorithm for a directed graph; here, the underlying graph
    ///     consisting of the pixels and four edges connecting each pixel
    ///     to its 4-neighbor is a special case of a directed graph, where
    ///     each edge is bi-directional.  The implementation of this generalized
    ///     maze search is left as an exercise to the reader.
    ///
    ///     Let's generalize a bit further.  Suppose the "maze" is just
    ///     a grayscale image -- think of it as an elevation map.  The cost
    ///     of moving on this surface depends on the height, or the gradient,
    ///     or whatever you want.  All that is required is that the cost
    ///     is specified and non-negative on each link between adjacent
    ///     pixels.  Now the problem becomes: find the least cost path
    ///     moving on this surface between two specified end points.
    ///     For example, if the cost across an edge between two pixels
    ///     depends on the "gradient", you can use:
    ///          cost = 1 + L_ABSdeltaV
    ///     where deltaV is the difference in value between two adjacent
    ///     pixels.  If the costs are all integers, we can still use an array
    ///     of stacks to avoid ordering the queue e.g., by using a heap sort.
    ///     This is a neat problem, because you don't even have to build a
    ///     maze -- you can can use it on any grayscale image!
    ///
    ///     Rather than using an array of stacks, a more practical
    ///     approach is to implement with a priority queue, which is
    ///     a queue that is sorted so that the elements with the largest
    ///     or smallest key values always come off first.  The
    ///     priority queue is efficiently implemented as a heap, and
    ///     this is how we do it.  Suppose you run the algorithm
    ///     using a priority queue, doing the bookkeeping with an
    ///     auxiliary image data structure that saves the distance of
    ///     each pixel put on the queue as before, according to the method
    ///     described above.  We implement it as a 2-way choice by
    ///     initializing the distance array to a large value and putting
    ///     a pixel on the queue if its distance is less than the value
    ///     found on the array.  When you finally pop the end pixel from
    ///     the queue, you're done, and you can trace the path backward,
    ///     either always going downhill or using an auxiliary image to
    ///     give you the direction to go at each step.  This is implemented
    ///     here in searchGrayMaze.
    ///
    ///     Do we really have to use a sorted queue?  Can we solve this
    ///     generalized maze with an unsorted queue of pixels?  Or even
    ///     an unsorted stack, doing a depth-first search (DFS)?
    ///     Consider a different algorithm for this generalized maze, where
    ///     we travel again breadth first, but this time use a single,
    ///     unsorted queue.  An auxiliary image is used as before to
    ///     store the distances and to determine if pixels get pushed
    ///     on the stack or dropped.  As before, we must allow pixels
    ///     to be revisited, with relaxation of the distance if a shorter
    ///     path arrives later.  As a result, we will in general have
    ///     multiple instances of the same pixel on the stack with different
    ///     distances.  However, because the queue is not ordered, some of
    ///     these pixels will be popped when another instance with a lower
    ///     distance is still on the stack.  Here, we're just popping them
    ///     in the order they go on, rather than setting up a priority
    ///     based on minimum distance.  Thus, unlike the priority queue,
    ///     when a pixel is popped we have to check the distance map to
    ///     see if a pixel with a lower distance has been put on the queue,
    ///     and, if so, we discard the pixel we just popped.  So the
    ///     "while" loop looks like this:
    ///       ~ pop a pixel from the queue
    ///       ~ check its distance against the distance stored in the
    ///         distance map; if larger, discard
    ///       ~ otherwise, for each of its neighbors:
    ///           ~ compute its distance from the start pixel
    ///           ~ compare this distance with that on the distance map:
    ///               ~ if the distance map value higher, relax the distance
    ///                 and push the pixel on the queue
    ///               ~ if the distance map value is lower, discard the pixel
    ///
    ///     How does this loop terminate?  Before, with an ordered queue,
    ///     it terminates when you pop the end pixel.  But with an unordered
    ///     queue or stack, the first time you hit the end pixel, the
    ///     distance is not guaranteed to be correct, because the pixels
    ///     along the shortest path may not have yet been visited and relaxed.
    ///     Because the shortest path can theoretically go anywhere,
    ///     we must keep going.  How do we know when to stop?   Dijkstra
    ///     uses an ordered queue to systematically remove nodes from
    ///     further consideration.  Each time a pixel is popped, we're
    ///     done with it; it's "finalized" in the Dijkstra sense because
    ///     we know the shortest path to it.  However, with an unordered
    ///     queue, the brute force answer is: stop when the queue
    ///     or stack is empty, because then every pixel in the image
    ///     has been assigned its minimum "distance" from the start pixel.
    ///
    ///     This is similar to the situation when you use a stack for the
    ///     simpler uniform-step problem: with breadth-first search BFS
    ///     the pixels on the queue are automatically ordered, so you are
    ///     done when you locate the end pixel as a neighbor of a popped pixel;
    ///     whereas depth-first search DFS, using a stack, requires,
    ///     in general, a search of every accessible pixel.  Further, if
    ///     a pixel is revisited with a smaller distance, that distance is
    ///     recorded and the pixel is put on the stack again.
    ///
    ///     But surely, you ask, can't we stop sooner?  What if the
    ///     start and end pixels are very close to each other?
    ///     OK, suppose they are, and you have very high walls and a
    ///     long snaking level path that is actually the minimum cost.
    ///     That long path can wind back and forth across the entire
    ///     maze many times before ending up at the end point, which
    ///     could be just over a wall from the start.  With the unordered
    ///     queue, you very quickly get a high distance for the end
    ///     pixel, which will be relaxed to the minimum distance only
    ///     after all the pixels of the path have been visited and placed
    ///     on the queue, multiple times for many of them.  So that's the
    ///     price for not ordering the queue!
    ///
    /// (The C code does not check the end point; it must be inside the image,
    /// otherwise the C function reads out of bounds, so it is checked here.)
    /// </remarks>
    public Pta SearchGrayMaze(int xi, int yi, int xf, int yf, out Pix pixd)
    {
        const string func = "pixSearchGrayMaze";
        Pix pixs = this;
        pixs.GetDimensions(out int w, out int h, out int d);
        if (w < 50 || h < 50)
            throw Lept.Arg(func, "too small: w and h not >= 50");
        if (d != 8)
            throw Lept.Arg(func, "pixs not 8 bpp");
        if (xi <= 0 || xi >= w)
            throw Lept.Arg(func, "xi not valid");
        if (yi <= 0 || yi >= h)
            throw Lept.Arg(func, "yi not valid");
        if (xf < 0 || xf >= w || yf < 0 || yf >= h)
            throw Lept.Arg(func, "end point not in the image");

            /* Allocate stuff */
        Pix pixr = Create(w, h, 32);  /* minimum distance to pixels already visited */
        pixr.SetAll();  /* initialize to max value */
        Pix pixp = Create(w, h, 8);  /* direction to parent stored as enum val */
        uint[] lines8 = pixs.Data;
        uint[] linep8 = pixp.Data;
        uint[] liner32 = pixr.Data;
        int wpls = pixs.Wpl, wplp = pixp.Wpl, wplr = pixr.Wpl;
        L_Heap lh = L_Heap.Create(0, L.L_SORT_INCREASING);  /* always remove closest pixels */

            /* Prime the heap with the first pixel */
        pixs.GetPixel(xi, yi, out uint val);
        MazeEl el = MazeelCreate(xi, yi, 0);  /* don't need direction here */
        el.distance = 0;
        pixs.GetPixel(xi, yi, out val);
        el.val = val;
        pixr.SetPixel(xi, yi, 0);  /* distance is 0 */
        lh.Add(el);

            /* Breadth-first search with priority queue (implemented by
               a heap), labeling direction to parents in pixp and minimum
               distance to visited pixels in pixr.  Stop when we pull
               the destination point (xf, yf) off the queue. */
        while (lh.Count > 0) {
            MazeEl? elp = (MazeEl?)lh.Remove();
            if (elp == null) {
                Log.Error(func, "heap broken!!");
                break;
            }
            int x = elp.x;
            int y = elp.y;
            if (x == xf && y == yf)   /* exit condition */
                break;
            int distparent = (int)elp.distance;
            val = elp.val;
            int sival = (int)val;

            if (x > 0) {  /* check to west */
                TryStep(x - 1, y, DIR_EAST);  /* parent to E */
            }
            if (y > 0) {  /* check north */
                TryStep(x, y - 1, DIR_SOUTH);  /* parent to S */
            }
            if (x < w - 1) {  /* check east */
                TryStep(x + 1, y, DIR_WEST);  /* parent to W */
            }
            if (y < h - 1) {  /* check south */
                TryStep(x, y + 1, DIR_NORTH);  /* parent to N */
            }

            void TryStep(int nx, int ny, int parentdir)
            {
                uint vals = (uint)ArrayAccess.GetDataByte(lines8.AsSpan(ny * wpls, wpls), nx);
                uint valr = ArrayAccess.GetDataFourBytes(liner32.AsSpan(ny * wplr, wplr), nx);
                int sivals = (int)vals;
                int cost = 1 + Math.Abs(sivals - sival);  /* cost to move to this pixel */
                int dist = distparent + cost;
                if ((uint)dist < valr) {  /* shortest path so far to this pixel */
                    ArrayAccess.SetDataFourBytes(liner32.AsSpan(ny * wplr, wplr), nx, (uint)dist);  /* new dist */
                    ArrayAccess.SetDataByte(linep8.AsSpan(ny * wplp, wplp), nx, parentdir);
                    MazeEl nel = MazeelCreate(nx, ny, 0);
                    nel.val = vals;
                    nel.distance = dist;
                    lh.Add(nel);
                }
            }
        }

        pixd = pixs.Convert8To32();
        ComposeRGBPixel(255, 0, 0, out uint rpixel);  /* start point */
        ComposeRGBPixel(0, 255, 0, out uint gpixel);
        ComposeRGBPixel(0, 0, 255, out uint bpixel);  /* end point */

        int px = xf;
        int py = yf;
        Pta pta = Pta.Create(0);
        while (true) {  /* write path onto pixd */
            pta.AddPt(px, py);
            if (px == xi && py == yi)
                break;
            pixd.SetPixel(px, py, gpixel);
            pixp.GetPixel(px, py, out val);
            if (val == DIR_NORTH)
                py--;
            else if (val == DIR_SOUTH)
                py++;
            else if (val == DIR_EAST)
                px++;
            else if (val == DIR_WEST)
                px--;
        }
        pixd.SetPixel(xi, yi, rpixel);
        pixd.SetPixel(xf, yf, bpixel);

        return pta;
    }
}
