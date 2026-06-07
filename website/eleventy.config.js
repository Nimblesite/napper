import techdoc from "eleventy-plugin-techdoc";

export default function (eleventyConfig) {
  eleventyConfig.addPlugin(techdoc, {
    site: {
      name: "Napper",
      url: "https://napperapi.dev",
      description:
        "CLI-first, test-oriented HTTP API testing tool for VS Code, Zed, and any editor — script in JavaScript, Python, F#, or C#.",
      author: "Christian Findlay",
      themeColor: "#1B4965",
      stylesheet: "/assets/css/styles.css",
      ogImage: "/assets/images/screenshot-playlist.png",
      organization: {
        name: "Napper",
        url: "https://napperapi.dev",
        logo: "/assets/images/logo.png",
        sameAs: [
          "https://github.com/Nimblesite/napper",
          "https://marketplace.visualstudio.com/items?itemName=nimblesite.napper",
        ],
      },
    },
    features: {
      blog: true,
      docs: true,
      darkMode: true,
      i18n: false,
    },
  });

  eleventyConfig.addPassthroughCopy("src/assets");
  eleventyConfig.addPassthroughCopy({ "src/favicon.ico": "favicon.ico" });

  const faviconLinks = [
    '<link rel="icon" type="image/x-icon" href="/favicon.ico">',
    '<link rel="icon" type="image/png" sizes="32x32" href="/assets/images/favicon-32x32.png">',
    '<link rel="icon" type="image/png" sizes="16x16" href="/assets/images/favicon-16x16.png">',
    '<link rel="apple-touch-icon" sizes="180x180" href="/assets/images/apple-touch-icon.png">',
  ].join("\n  ");

  eleventyConfig.addTransform("favicon", function (content) {
    if (this.page.outputPath?.endsWith(".html")) {
      return content.replace("</head>", `  ${faviconLinks}\n</head>`);
    }
    return content;
  });

  // Fix robots.txt: allow image crawling (Google requires image access)
  eleventyConfig.addTransform("robots-fix", function (content) {
    if (this.page.outputPath === "robots.txt" || this.page.outputPath?.endsWith("/robots.txt")) {
      return content.replace(
        "Disallow: /assets/",
        "Disallow: /assets/css/\nDisallow: /assets/js/"
      );
    }
    return content;
  });

  // Turn the footer copyright line into a copyright link to Nimblesite.
  const copyrightYear = new Date().getFullYear();
  eleventyConfig.addTransform("footer-branding", function (content) {
    if (this.page.outputPath?.endsWith(".html")) {
      return content.replace(
        `<p>&copy; ${copyrightYear} Napper</p>`,
        `<p>&copy; ${copyrightYear} Napper — built by <a href="https://nimblesite.co" rel="author">Nimblesite</a></p>`
      );
    }
    return content;
  });

  // Append site-name branding to bare page titles (improves SERP/AI branding).
  eleventyConfig.addTransform("title-branding", function (content) {
    if (!this.page.outputPath?.endsWith(".html")) {
      return content;
    }
    const match = content.match(/<title>([^<]*)<\/title>/);
    if (!match || match[1].includes("Napper")) {
      return content;
    }
    const bare = match[1];
    const branded = `${bare} — Napper`;
    return content
      .replace(`<title>${bare}</title>`, `<title>${branded}</title>`)
      .replace(`<meta name="title" content="${bare}">`, `<meta name="title" content="${branded}">`)
      .replace(`<meta property="og:title" content="${bare}">`, `<meta property="og:title" content="${branded}">`)
      .replace(`<meta name="twitter:title" content="${bare}">`, `<meta name="twitter:title" content="${branded}">`);
  });

  // Fix OG site_name: use short name instead of full title
  eleventyConfig.addTransform("og-site-name", function (content) {
    if (this.page.outputPath?.endsWith(".html")) {
      return content.replace(
        '<meta property="og:site_name" content="Napper — CLI-First API Testing for VS Code, Zed and Any Editor">',
        '<meta property="og:site_name" content="Napper">'
      );
    }
    return content;
  });

  // Replace techdoc generator tag with project branding
  eleventyConfig.addTransform("generator-tag", function (content) {
    if (this.page.outputPath?.endsWith(".html")) {
      return content.replace(
        '<meta name="generator" content="Eleventy + techdoc">',
        '<meta name="generator" content="Eleventy">'
      );
    }
    return content;
  });

  // Fix llms.txt: remove dead /api/ link
  eleventyConfig.addTransform("llms-fix", function (content) {
    if (this.page.outputPath === "llms.txt" || this.page.outputPath?.endsWith("/llms.txt")) {
      return content.replace(
        "- API Reference: https://napperapi.dev/api/",
        ""
      );
    }
    return content;
  });

  return {
    dir: {
      input: "src",
      output: "_site",
      includes: "_includes",
      data: "_data",
    },
    templateFormats: ["md", "njk", "html"],
    markdownTemplateEngine: "njk",
    htmlTemplateEngine: "njk",
  };
}
