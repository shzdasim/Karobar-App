// Shared Logo component for all sidebar templates
import React from "react";

const logoCandidates = ["/storage/logos/logo.png", "/logo.png"];

export default function Logo({ className = "h-8 w-8", alt = "Karobar App" }) {
  return (
    <picture>
      {logoCandidates.map((src) => (
        <img
          key={src}
          src={src}
          alt={alt}
          className={`${className} object-contain rounded hidden`}
          onLoad={(e) => {
            const imgs = e.currentTarget.parentElement.querySelectorAll("img");
            imgs.forEach((im) => (im.style.display = "none"));
            e.currentTarget.style.display = "block";
          }}
        />
      ))}
    </picture>
  );
}

