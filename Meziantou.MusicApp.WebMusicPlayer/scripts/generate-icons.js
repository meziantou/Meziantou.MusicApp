import sharp from 'sharp';
import { fileURLToPath } from 'url';
import { dirname, join } from 'path';

const __filename = fileURLToPath(import.meta.url);
const __dirname = dirname(__filename);

const SOURCE_ICON = join(__dirname, '../public/pwa-512x512.svg');
const PUBLIC_DIR = join(__dirname, '../public');

async function generateIcons() {
  console.log('Generating icons...');

  try {
    // Generate 192x192 PNG
    await sharp(SOURCE_ICON)
      .resize(192, 192)
      .png()
      .toFile(join(PUBLIC_DIR, 'pwa-192x192.png'));
    console.log('Generated pwa-192x192.png');

    // Generate 512x512 PNG
    await sharp(SOURCE_ICON)
      .resize(512, 512)
      .png()
      .toFile(join(PUBLIC_DIR, 'pwa-512x512.png'));
    console.log('Generated pwa-512x512.png');

    console.log('Icon generation complete!');
  } catch (error) {
    console.error('Error generating icons:', error);
    process.exit(1);
  }
}

generateIcons();
