using UnityEngine;
using Unity.Collections;
using System.Diagnostics;

// Assets/Game/Phase1Validator.cs
public class Phase1Validator : MonoBehaviour
{
    public ComputeShader readTestShader;
    private GraphicsBuffer _bufferLock;
    private GraphicsBuffer _bufferSet;
    private GraphicsBuffer _resultBuffer;
    private NativeArray<uint> _payload;

    // ~8MB of uint (2,000,000 * 4 bytes) - comfortably past the M1's GPU cache,
    // so LockBufferForWrite vs SetData placement actually matters for read speed.
    // The old 10,000-element (40KB) buffer fit entirely in cache either way,
    // which is why it couldn't show a real difference between the two paths.
    private const int ELEMENT_COUNT = 2_000_000;
    private const int THREADS_PER_GROUP = 64; // must match [numthreads(64,1,1)] in the kernel
    private int _groupCount;

    void Start()
    {
        _groupCount = Mathf.CeilToInt(ELEMENT_COUNT / (float)THREADS_PER_GROUP);

        _bufferLock = new GraphicsBuffer(GraphicsBuffer.Target.Structured, GraphicsBuffer.UsageFlags.LockBufferForWrite, ELEMENT_COUNT, 4);
        _bufferSet = new GraphicsBuffer(GraphicsBuffer.Target.Structured, GraphicsBuffer.UsageFlags.None, ELEMENT_COUNT, 4);
        // One result slot per thread, not per group - avoids any out-of-bounds writes
        // once the dispatch is scaled up from the old fixed (1,1,1) call.
        _resultBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, GraphicsBuffer.UsageFlags.None, _groupCount * THREADS_PER_GROUP, 4);

        _payload = new NativeArray<uint>(ELEMENT_COUNT, Allocator.Persistent);
        for (int i = 0; i < ELEMENT_COUNT; i++) _payload[i] = (uint)i;

        int kernel = readTestShader.FindKernel("CSMain");
        readTestShader.SetInt("ElementCount", ELEMENT_COUNT);
        readTestShader.SetBuffer(kernel, "ResultBuffer", _resultBuffer);

        // Upload once via each path at startup - we are timing STEADY-STATE READS,
        // matching the real clipmap's "written rarely, read millions of times" pattern (§3.7).
        NativeArray<uint> lockArr = _bufferLock.LockBufferForWrite<uint>(0, ELEMENT_COUNT);
        lockArr.CopyFrom(_payload);
        _bufferLock.UnlockBufferAfterWrite<uint>(ELEMENT_COUNT);

        _bufferSet.SetData(_payload);

        // One warm-up dispatch per path to absorb first-dispatch PSO/pipeline-compile cost
        // before any timed run - otherwise the first capture you take will be contaminated
        // by one-time compile overhead that has nothing to do with buffer placement.
        readTestShader.SetBuffer(kernel, "ReadBuffer", _bufferLock);
        readTestShader.Dispatch(kernel, _groupCount, 1, 1);
        readTestShader.SetBuffer(kernel, "ReadBuffer", _bufferSet);
        readTestShader.Dispatch(kernel, _groupCount, 1, 1);
    }

    // Held (not pressed) so a dispatch fires every single frame while held down -
    // guarantees something is always in flight the instant you click Capture in Xcode,
    // instead of racing a single keypress against the capture arming window.
    void Update()
    {
        if (Input.GetKey(KeyCode.L)) { RunLockBufferDispatch();  }
        if (Input.GetKey(KeyCode.S)) RunSetDataDispatch();
    }

    private void RunLockBufferDispatch()
    {
        int kernel = readTestShader.FindKernel("CSMain");
        readTestShader.SetBuffer(kernel, "ReadBuffer", _bufferLock);
        readTestShader.SetBuffer(kernel, "ResultBuffer", _resultBuffer);
        // Single dispatch covering the whole buffer - NOT a thousand (1,1,1) calls.
        readTestShader.Dispatch(kernel, _groupCount, 1, 1);
    }

    private void RunSetDataDispatch()
    {
        int kernel = readTestShader.FindKernel("CSMain");
        readTestShader.SetBuffer(kernel, "ReadBuffer", _bufferSet);
        readTestShader.SetBuffer(kernel, "ResultBuffer", _resultBuffer);
        readTestShader.Dispatch(kernel, _groupCount, 1, 1);
    }

    void OnDestroy()
    {
        _bufferLock?.Dispose();
        _bufferSet?.Dispose();
        _resultBuffer?.Dispose();
        if (_payload.IsCreated) _payload.Dispose();
    }
}