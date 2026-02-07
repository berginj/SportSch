import { defineCollection, z } from "astro:content";

const news = defineCollection({
  type: "content",
  schema: z.object({
    title: z.string(),
    summary: z.string(),
    publishDate: z.string(),
    featured: z.boolean().default(false),
  }),
});

const policies = defineCollection({
  type: "content",
  schema: z.object({
    title: z.string(),
    summary: z.string(),
    order: z.number(),
    lastUpdated: z.string(),
  }),
});

export const collections = { news, policies };
