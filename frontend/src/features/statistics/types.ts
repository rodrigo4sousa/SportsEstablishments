import type { components } from "@/lib/api/schema";

type Schemas = components["schemas"];

/** The numbers of a pitch, a club or a total over a period (US14); the API sends numbers, the generated types also allow strings. */
export type Figures = {
  bookedHours: number;
  openHours: number;
  /** A fraction: 0.8125 is 81,25 %. */
  occupation: number;
  revenue: number;
  confirmedBookings: number;
  noShows: number;
};

export type PitchStatistics = Figures & { pitchId: string; name: string; sport: string; status: string };

export type ClubStatistics = { clubId: string; clubName: string; from: string; to: string; pitches: PitchStatistics[]; total: Figures };

export type OwnerClubLine = Figures & { clubId: string; clubName: string };

export type OwnerStatistics = { from: string; to: string; clubs: OwnerClubLine[]; total: Figures };

function figures(value: Schemas["OccupationFiguresResponse"]): Figures {
  return {
    bookedHours: Number(value.bookedHours),
    openHours: Number(value.openHours),
    occupation: Number(value.occupation),
    revenue: Number(value.revenue),
    confirmedBookings: Number(value.confirmedBookings),
    noShows: Number(value.noShows),
  };
}

export function toClubStatistics(response: Schemas["ClubStatisticsResponse"]): ClubStatistics {
  return {
    clubId: response.clubId,
    clubName: response.clubName,
    from: response.from,
    to: response.to,
    pitches: response.pitches.map((pitch) => ({ pitchId: pitch.pitchId, name: pitch.name, sport: pitch.sport, status: pitch.status, ...figures(pitch) })),
    total: figures(response.total),
  };
}

export function toOwnerStatistics(response: Schemas["OwnerStatisticsResponse"]): OwnerStatistics {
  return {
    from: response.from,
    to: response.to,
    clubs: response.clubs.map((club) => ({ clubId: club.clubId, clubName: club.clubName, ...figures(club) })),
    total: figures(response.total),
  };
}
