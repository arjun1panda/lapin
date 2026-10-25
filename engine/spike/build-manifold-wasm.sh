#!/bin/sh
# manifold's C API (Apache-2.0) as static libraries for the browser engine:
# the same tag and options as scripts/build-manifold.mjs (manifold, Clipper2,
# no TBB), compiled by the Emscripten the .NET wasm-tools workload brings, with
# its exception model (-fwasm-exceptions) so it links into the .NET runtime.
# Output: engine/spike/native/browser-wasm/{libmanifoldc,libmanifold,libClipper2}.a
set -e
DOTNET_ROOT=${DOTNET_ROOT:-$HOME/.dotnet-wasm}
P=$DOTNET_ROOT/packs
SDK=$(ls -d $P/Microsoft.NET.Runtime.Emscripten.3.1.56.Sdk.*/*/tools | head -1)
NODE=$(ls $P/Microsoft.NET.Runtime.Emscripten.3.1.56.Node.*/*/tools/bin/node | head -1)
PY=$(ls -d $P/Microsoft.NET.Runtime.Emscripten.3.1.56.Python.*/*/tools/bin | head -1)
CACHE=$(ls -d $P/Microsoft.NET.Runtime.Emscripten.3.1.56.Cache.*/*/tools/emscripten/cache | head -1)
export EMSDK_PATH=$SDK DOTNET_EMSCRIPTEN_LLVM_ROOT=$SDK/bin DOTNET_EMSCRIPTEN_BINARYEN_ROOT=$SDK DOTNET_EMSCRIPTEN_NODE_JS=$NODE EM_CACHE=$CACHE
export PATH=$SDK/emscripten:$SDK/bin:$PY:$PATH
TAG=v3.2.1
WORK=${TMPDIR:-/tmp}/lapin-manifold-$TAG-wasm
OUT=$(cd "$(dirname "$0")" && pwd)/native/browser-wasm
[ -d $WORK/src ] || git clone --depth 1 --branch $TAG https://github.com/elalish/manifold.git $WORK/src
rm -rf $WORK/build
FLAGS="-fwasm-exceptions -msimd128 -O3"
emcmake cmake -S $WORK/src -B $WORK/build -DCMAKE_BUILD_TYPE=Release -DBUILD_SHARED_LIBS=OFF -DMANIFOLD_TEST=OFF \
  -DMANIFOLD_PAR=OFF -DMANIFOLD_CBIND=ON -DMANIFOLD_CROSS_SECTION=ON -DMANIFOLD_USE_BUILTIN_CLIPPER2=ON -DMANIFOLD_PYBIND=OFF \
  -DMANIFOLD_JSBIND=OFF -DCMAKE_C_FLAGS="$FLAGS" -DCMAKE_CXX_FLAGS="$FLAGS"
cmake --build $WORK/build --target manifoldc -j 8
mkdir -p $OUT
for a in $(find $WORK/build -name "libmanifoldc.a" -o -name "libmanifold.a" -o -name "libClipper2.a"); do cp $a $OUT/; done
# The P/Invoke table names a static library by its file name: manifoldc, as ManifoldBooleans imports it.
mv $OUT/libmanifoldc.a $OUT/manifoldc.a
ls -la $OUT
