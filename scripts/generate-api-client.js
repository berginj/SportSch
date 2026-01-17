#!/usr/bin/env node

/**
 * Generate TypeScript API client from OpenAPI spec
 *
 * Usage:
 *   node scripts/generate-api-client.js [spec-url]
 *
 * Default spec URL: http://localhost:7071/api/swagger.json
 */

import { generate } from 'openapi-typescript-codegen';
import { readFileSync, existsSync } from 'fs';
import { fileURLToPath } from 'url';
import { dirname, join } from 'path';

const __filename = fileURLToPath(import.meta.url);
const __dirname = dirname(__filename);
const projectRoot = join(__dirname, '..');

const specUrl = process.argv[2] || 'http://localhost:7071/api/swagger.json';
const fallbackSpecPath = join(projectRoot, 'api-spec.json');
const outputDir = join(projectRoot, 'src/generated-api');

async function fetchSpec(url) {
  try {
    console.log(`Fetching OpenAPI spec from ${url}...`);
    const response = await fetch(url);

    if (!response.ok) {
      throw new Error(`HTTP ${response.status}: ${response.statusText}`);
    }

    const spec = await response.json();
    console.log('✓ Fetched OpenAPI spec successfully');
    return spec;
  } catch (error) {
    console.error(`✗ Failed to fetch from ${url}:`, error.message);

    // Try fallback file
    if (existsSync(fallbackSpecPath)) {
      console.log(`Using fallback spec file: ${fallbackSpecPath}`);
      const spec = JSON.parse(readFileSync(fallbackSpecPath, 'utf-8'));
      return spec;
    }

    throw new Error('No OpenAPI spec available. Please start the API or provide a spec file.');
  }
}

async function generateClient() {
  try {
    console.log('🚀 Starting TypeScript API client generation...\n');

    const spec = await fetchSpec(specUrl);

    console.log('Generating TypeScript client...');
    await generate({
      input: spec,
      output: outputDir,
      httpClient: 'fetch',
      clientName: 'SportSchApiClient',
      useOptions: true,
      useUnionTypes: true,
      exportCore: true,
      exportServices: true,
      exportModels: true,
      exportSchemas: false,
      indent: '2',
      postfixServices: 'Service',
      postfixModels: '',
    });

    console.log(`\n✓ TypeScript API client generated successfully!`);
    console.log(`  Output directory: ${outputDir}`);
    console.log(`\nNext steps:`);
    console.log(`  1. Import the client in your code:`);
    console.log(`     import { SportSchApiClient } from './generated-api';`);
    console.log(`  2. Configure the base URL:`);
    console.log(`     SportSchApiClient.OpenAPI.BASE = 'http://localhost:7071';`);
    console.log(`  3. Use the generated services:`);
    console.log(`     const slots = await SlotsService.getSlots({ leagueId: '...' });`);

  } catch (error) {
    console.error('\n✗ Error generating API client:', error.message);
    console.error('\nTroubleshooting:');
    console.error('  1. Make sure the API is running: cd api && func start');
    console.error('  2. Verify the spec URL is accessible');
    console.error('  3. Check that OpenAPI attributes are on all functions');
    process.exit(1);
  }
}

generateClient();
