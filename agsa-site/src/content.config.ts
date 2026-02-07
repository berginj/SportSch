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

export const collections = { news };
