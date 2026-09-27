// Developer-only: npm install --no-save sharp, then node tools/Create-AppIcon.cjs.
// SVG is the editable source. PNG-compressed ICO frames support Windows 10/11.
const fs = require('node:fs');
const path = require('node:path');
const sharp = require('sharp');
async function main() {
  const directory = path.resolve(__dirname, '../assets/branding');
  const svg = fs.readFileSync(path.join(directory, 'app-icon.svg'));
  await sharp(svg).resize(512, 512).png().toFile(path.join(directory, 'app-icon.png'));
  const sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
  const frames = await Promise.all(sizes.map(size => sharp(svg).resize(size, size).png().toBuffer()));
  const header = Buffer.alloc(6 + sizes.length * 16);
  header.writeUInt16LE(1, 2);
  header.writeUInt16LE(sizes.length, 4);
  let offset = header.length;
  frames.forEach((frame, index) => {
    const entry = 6 + index * 16;
    header[entry] = sizes[index] % 256;
    header[entry + 1] = sizes[index] % 256;
    header.writeUInt16LE(1, entry + 4);
    header.writeUInt16LE(32, entry + 6);
    header.writeUInt32LE(frame.length, entry + 8);
    header.writeUInt32LE(offset, entry + 12);
    offset += frame.length;
  });
  fs.writeFileSync(path.join(directory, 'app-icon.ico'), Buffer.concat([header, ...frames]));
  console.log(`Created editable-source app icon with ${sizes.length} Windows sizes.`);
}
main().catch(error => { console.error(error); process.exitCode = 1; });
