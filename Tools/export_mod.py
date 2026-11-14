"""Export StellarStriderLib as a ZIP containing only Mods/StellarStriderLib.dll and mod.json."""
import argparse
import json
from pathlib import Path
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dll', type=Path, default=ROOT / 'StellarStriderLib/bin/Release/netstandard2.1/StellarStriderLib.dll')
    parser.add_argument('--output', type=Path, default=ROOT / 'Releases/StellarStriderLib.zip')
    parser.add_argument('--build', action='store_true', help='Build Release before exporting')
    args = parser.parse_args()
    if args.build:
        subprocess.run(['dotnet', 'build', str(ROOT / 'StellarStriderLib/StellarStriderLib.csproj'),
                        '-c', 'Release', '-p:SD_MODS_DIR='], cwd=ROOT, check=True)
    manifest = ROOT / 'mod.json'
    for source in (args.dll, manifest):
        if not source.is_file():
            parser.error('Missing input: ' + str(source))
        if args.output.resolve() == source.resolve():
            parser.error('Output must not overwrite an input file')
    json.loads(manifest.read_text(encoding='utf-8'))
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(args.output, 'w', zipfile.ZIP_DEFLATED) as archive:
        archive.write(args.dll, 'Mods/StellarStriderLib.dll')
        archive.write(manifest, 'mod.json')
    print('Exported:', args.output)


if __name__ == '__main__':
    main()
